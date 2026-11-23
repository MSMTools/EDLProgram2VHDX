namespace QualcommEDLProgramStream
{
    internal class FlashPartStream : Stream
    {
        private readonly long length;

        private readonly List<FlashPart> flashParts;
        private long currentPosition;
        private bool disposed;

        public override bool CanRead => !disposed;

        public override bool CanSeek => !disposed;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => currentPosition;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                currentPosition = value;
            }
        }

        public FlashPartStream(ulong totalLength, List<FlashPart> flashParts)
        {
            length = checked((long)totalLength);
            this.flashParts = flashParts.OrderBy(part => part.LocationOnDisk).ToList();
            ulong previousEnd = 0;
            foreach (FlashPart part in this.flashParts)
            {
                if (part.FileOffset < 0 || part.Length < 0 || part.FileOffset > part.Data.Length ||
                    part.Length > part.Data.Length - part.FileOffset || part.LocationOnDisk > totalLength ||
                    (ulong)part.Length > totalLength - part.LocationOnDisk)
                {
                    throw new InvalidDataException("Flash part exceeds its source file or the disk capacity.");
                }
                if (part.Length == 0)
                {
                    continue;
                }
                if (part.LocationOnDisk < previousEnd)
                {
                    throw new InvalidDataException("Flash parts overlap on disk.");
                }
                previousEnd = part.LocationOnDisk + (ulong)part.Length;
            }
        }

        public override void Flush()
        {
            // Nothing to do here
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (count > buffer.Length - offset)
            {
                throw new ArgumentException("Buffer range is out of bounds.");
            }
            if (currentPosition >= length)
            {
                return 0;
            }

            int bytesToRead = (int)Math.Min(count, length - currentPosition);
            Array.Clear(buffer, offset, bytesToRead);
            long readEnd = currentPosition + bytesToRead;
            foreach (FlashPart part in flashParts)
            {
                long partStart = (long)part.LocationOnDisk;
                if (partStart >= readEnd)
                {
                    break;
                }
                long partEnd = partStart + part.Length;
                if (partEnd <= currentPosition)
                {
                    continue;
                }

                long start = Math.Max(currentPosition, partStart);
                int toRead = (int)(Math.Min(readEnd, partEnd) - start);
                part.Data.Position = part.FileOffset + (start - partStart);
                part.Data.ReadExactly(buffer.AsSpan(offset + (int)(start - currentPosition), toRead));
            }

            currentPosition = readEnd;
            return bytesToRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(Position + offset),
                SeekOrigin.End => checked(Length + offset),
                _ => throw new ArgumentException(nameof(origin))
            };

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                foreach (FlashPart part in flashParts)
                {
                    part.Data.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
