using System.Reflection.PortableExecutable;
using System.Text;

namespace UpdateDeck.Windows;

/// <summary>Inspects named PE exports as file data; never maps or loads a DLL.</summary>
public static class PeExports
{
    public static bool Contains(string file,params string[] expected)
    {
        try
        {
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            if(stream.Length is <64 or >536870912)return false;
            using var pe=new PEReader(stream,PEStreamOptions.LeaveOpen);var headers=pe.PEHeaders;
            if(headers.PEHeader==null||headers.SectionHeaders.Length is 0 or >96)return false;
            long? Offset(uint rva,long length)
            {
                foreach(var section in headers.SectionHeaders)
                {
                    long relative=(long)rva-section.VirtualAddress;
                    if(relative<0||relative+length>section.SizeOfRawData)continue;
                    long offset=section.PointerToRawData+relative;
                    if(offset<0||offset+length>stream.Length)return null;return offset;
                }
                return null;
            }
            var directory=headers.PEHeader.ExportTableDirectory;
            if(directory.RelativeVirtualAddress==0||directory.Size<40||Offset((uint)directory.RelativeVirtualAddress,40) is not long exportOffset)return false;
            using var reader=new BinaryReader(stream,Encoding.ASCII,true);stream.Position=exportOffset+24;uint names=reader.ReadUInt32();
            stream.Position=exportOffset+32;uint nameRva=reader.ReadUInt32();
            if(names is 0 or >50000||Offset(nameRva,names*4L) is not long arrayOffset)return false;
            for(int i=0;i<names;i++)
            {
                stream.Position=arrayOffset+i*4L;uint rva=reader.ReadUInt32();if(Offset(rva,1) is not long position)continue;
                stream.Position=position;var name=new StringBuilder();bool terminated=false;
                for(uint n=0;n<256;n++)
                {
                    if(rva>uint.MaxValue-n||Offset(rva+n,1)!=position+n)break;
                    byte b=reader.ReadByte();if(b==0){terminated=true;break;}if(b>127)break;name.Append((char)b);
                }
                if(terminated&&expected.Contains(name.ToString(),StringComparer.Ordinal))return true;
            }
            return false;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException or InvalidOperationException){return false;}
    }
}
