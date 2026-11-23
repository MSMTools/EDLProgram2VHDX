using QualcommEDLProgramStream.XML;
using System.Globalization;

namespace QualcommEDLProgramStream
{
    public class ParseProgramXML
    {
        public static Stream? GetStream(string inputXmlStr, uint sectorSize)
        {
            if (sectorSize != 512 && sectorSize != 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(sectorSize));
            }

            Data data = XmlReader.DeserializeDataXmlFile(inputXmlStr);
            XML.Program? primaryGPT = data.Program?.FirstOrDefault(program => program.Label == "PrimaryGPT");
            if (primaryGPT == null)
            {
                return null;
            }

            string inputDirectory = Path.GetDirectoryName(Path.GetFullPath(inputXmlStr))!;
            byte[] gptBuffer = File.ReadAllBytes(Path.Combine(inputDirectory, primaryGPT.Filename));
            ulong gptFileOffset = checked(ParseSectorValue(primaryGPT.File_sector_offset, 0) * sectorSize);
            if (gptFileOffset > (ulong)gptBuffer.Length)
            {
                throw new InvalidDataException("Primary GPT file offset exceeds the file length.");
            }

            GPT.GPT gpt = new(gptFileOffset == 0 ? gptBuffer : gptBuffer[(int)gptFileOffset..], sectorSize);
            ulong diskSectors = checked(gpt.BackupSector + 1);
            ulong diskSize = checked(diskSectors * sectorSize);
            if (gpt.BackupSector <= gpt.LastUsableSector)
            {
                throw new InvalidDataException("Backup GPT is not beyond the usable sectors.");
            }

            Console.WriteLine($"Disk size: {diskSize}");
            List<FlashPart> flashingPartitions = [];
            try
            {
                foreach (XML.Program program in data.Program!)
                {
                    if (string.IsNullOrEmpty(program.Filename))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(program.SECTOR_SIZE_IN_BYTES) &&
                        ParseSectorValue(program.SECTOR_SIZE_IN_BYTES, diskSectors) != sectorSize)
                    {
                        throw new InvalidDataException($"Sector size mismatch for '{program.Filename}'.");
                    }

                    ulong startSector = ParseSectorValue(program.Start_sector, diskSectors);
                    ulong sectorCount = ParseSectorValue(program.Num_partition_sectors, diskSectors);
                    long fileOffset = checked((long)(ParseSectorValue(program.File_sector_offset, diskSectors) * sectorSize));
                    FileStream file = File.OpenRead(Path.Combine(inputDirectory, program.Filename));
                    try
                    {
                        if (fileOffset > file.Length)
                        {
                            throw new InvalidDataException($"File offset exceeds the length of '{program.Filename}'.");
                        }

                        long availableBytes = file.Length - fileOffset;
                        long length = sectorCount == 0 ? availableBytes : checked((long)(sectorCount * sectorSize));
                        if (length > availableBytes)
                        {
                            throw new InvalidDataException($"'{program.Filename}' is shorter than the declared sector range.");
                        }

                        flashingPartitions.Add(new FlashPart
                        {
                            LocationOnDisk = checked(startSector * sectorSize),
                            Data = file,
                            FileOffset = fileOffset,
                            Length = length
                        });
                    }
                    catch
                    {
                        file.Dispose();
                        throw;
                    }
                }

                return new FlashPartStream(diskSize, flashingPartitions);
            }
            catch
            {
                foreach (FlashPart part in flashingPartitions)
                {
                    part.Data.Dispose();
                }
                throw;
            }
        }

        private static ulong ParseSectorValue(string value, ulong diskSectors)
        {
            value = value.Trim().TrimEnd('.');
            const string diskToken = "NUM_DISK_SECTORS";
            if (!value.StartsWith(diskToken, StringComparison.Ordinal))
            {
                return ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
            }

            string adjustment = value[diskToken.Length..].Trim();
            if (adjustment.Length == 0)
            {
                return diskSectors;
            }

            char operation = adjustment[0];
            if (operation != '+' && operation != '-')
            {
                throw new FormatException($"Invalid sector expression '{value}'.");
            }

            ulong amount = ulong.Parse(adjustment[1..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
            return operation == '+' ? checked(diskSectors + amount) : checked(diskSectors - amount);
        }
    }
}
