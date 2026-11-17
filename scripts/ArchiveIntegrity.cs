using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace UpdateDeck.Packaging
{
    // Packaging-only helper. No archive paths are extracted or executed here.
    public static class ArchiveIntegrity
    {
        public sealed class EntryEvidence
        {
            public long Bytes {get;set;}
            public string Sha256 {get;set;}
            public byte[] Content {get;set;}
        }
        public sealed class PeEvidence
        {
            public string Machine {get;set;}
            public bool ManagedAnyCpu {get;set;}
        }
        public static PeEvidence InspectPe(byte[] content,int expectedMachine)
        {
            using(var reader=new PEReader(new MemoryStream(content,false)))
            {
                var headers=reader.PEHeaders;int machine=(int)headers.CoffHeader.Machine;
                if(headers.IsCoffOnly||headers.PEHeader==null)throw new InvalidDataException("A portable executable is a COFF object or lacks its image header.");
                if(machine==(int)Machine.I386&&headers.PEHeader.Magic!=PEMagic.PE32||
                    (machine==(int)Machine.Amd64||machine==(int)Machine.Arm64)&&headers.PEHeader.Magic!=PEMagic.PE32Plus)throw new InvalidDataException("PE machine and optional-header bitness disagree.");
                if(headers.CorHeader!=null&&(headers.CorHeader.Flags&CorFlags.Requires32Bit)!=0&&expectedMachine!=(int)Machine.I386)throw new InvalidDataException("A managed payload requires a 32-bit process in a 64-bit release.");
                bool anyCpu=machine==(int)Machine.I386&&headers.CorHeader!=null&&(headers.CorHeader.Flags&CorFlags.ILOnly)!=0&&(headers.CorHeader.Flags&CorFlags.Requires32Bit)==0;
                if(!anyCpu&&machine!=expectedMachine)throw new InvalidDataException("A portable PE file does not match the target architecture.");
                return new PeEvidence{Machine="0x"+machine.ToString("X4"),ManagedAnyCpu=anyCpu};
            }
        }
        private static readonly uint[] CrcTable=CreateCrcTable();
        public static void ValidatePaths(string[] names)
        {
            var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string name in names)
            {
                string path=name.TrimEnd('/');int separator=path.IndexOf('/');
                while(separator>=0)
                {string parent=path.Substring(0,separator);if(files.Contains(parent))throw new InvalidDataException("An archive file is also used as a parent directory.");directories.Add(parent);separator=path.IndexOf('/',separator+1);}
                if(name.EndsWith("/",StringComparison.Ordinal))
                {if(files.Contains(path))throw new InvalidDataException("An archive path names both a file and directory.");directories.Add(path);}
                else
                {if(directories.Contains(path)||!files.Add(path))throw new InvalidDataException("An archive path is duplicated or names both a file and directory.");}
            }
        }
        private static uint[] CreateCrcTable()
        {
            var table=new uint[256];
            for(uint i=0;i<table.Length;i++){uint value=i;for(int bit=0;bit<8;bit++)value=(value&1)!=0?0xedb88320u^(value>>1):value>>1;table[i]=value;}
            return table;
        }
        public static EntryEvidence Read(ZipArchiveEntry entry,long maximum,bool capture)
        {
            if(entry.Length<0||entry.Length>maximum||maximum<0)throw new InvalidDataException("Archive entry exceeds its supported size.");
            using(var stream=entry.Open())
            using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            using(var memory=capture?new MemoryStream():null)
            {
                byte[] buffer=ArrayPool<byte>.Shared.Rent(65536);long total=0;uint crc=0xffffffffu;
                try
                {
                    int count;
                    while((count=stream.Read(buffer,0,buffer.Length))>0)
                    {
                        total+=count;if(total>maximum||total>entry.Length)throw new InvalidDataException("Actual archive expansion exceeds its declared or supported size.");
                        hash.AppendData(buffer,0,count);if(memory!=null)memory.Write(buffer,0,count);
                        for(int i=0;i<count;i++)crc=CrcTable[(crc^buffer[i])&255]^(crc>>8);
                    }
                    if(total!=entry.Length)throw new InvalidDataException("Archive entry length differs from its metadata.");
                    if((~crc)!=entry.Crc32)throw new InvalidDataException("Archive entry CRC-32 does not match its content.");
                    return new EntryEvidence{Bytes=total,Sha256=Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),Content=memory==null?null:memory.ToArray()};
                }
                finally{ArrayPool<byte>.Shared.Return(buffer);}
            }
        }
        public static void ValidateJson(byte[] content)
        {
            using(var document=JsonDocument.Parse(content,new JsonDocumentOptions{MaxDepth=32}))ValidateElement(document.RootElement);
        }
        private static void ValidateElement(JsonElement element)
        {
            if(element.ValueKind==JsonValueKind.Object)
            {
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var property in element.EnumerateObject())
                {if(!names.Add(property.Name))throw new InvalidDataException("Duplicate or case-aliased JSON properties are ambiguous.");ValidateElement(property.Value);}
            }
            else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())ValidateElement(item);
        }
    }
}
