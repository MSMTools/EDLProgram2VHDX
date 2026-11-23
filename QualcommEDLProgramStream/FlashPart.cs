namespace QualcommEDLProgramStream
{
    internal class FlashPart
    {
        public Stream Data
        {
            get; set;
        }

        public ulong LocationOnDisk
        {
            get; set;
        }

        public long FileOffset
        {
            get; set;
        }

        public long Length
        {
            get; set;
        }
    }
}
