using System.Buffers.Binary;
using System.Text;

namespace HPD.Agent.Authority;

internal static class CanonicalCborValidatorV1
{
    private const int MaximumDepth=64;
    private const int MaximumItems=131072;
    private static readonly UTF8Encoding StrictUtf8=new(false,true);
    internal static bool IsValid(ReadOnlySpan<byte> bytes)
    {if(bytes.IsEmpty)return false;int offset=0,items=0;return Item(bytes,ref offset,0,ref items,out _)&&offset==bytes.Length;}
    private static bool Item(ReadOnlySpan<byte> bytes,ref int offset,int depth,ref int items,out ulong unsigned)
    {
        unsigned=0;if(depth>MaximumDepth||++items>MaximumItems||offset>=bytes.Length)return false;
        var initial=bytes[offset++];var major=initial>>5;var additional=initial&31;
        if(additional==31||!Argument(bytes,ref offset,additional,out var value))return false;
        if(additional==24&&value<24||additional==25&&value<=byte.MaxValue||additional==26&&value<=ushort.MaxValue||additional==27&&value<=uint.MaxValue)return false;
        switch(major)
        {
            case 0:unsigned=value;return true;
            case 1:return true;
            case 2:return Take(bytes,ref offset,value);
            case 3:
                if(value>int.MaxValue||!Take(bytes,ref offset,value,out var textBytes))return false;
                try{var text=StrictUtf8.GetString(textBytes);return text.IsNormalized(NormalizationForm.FormC);}catch(DecoderFallbackException){return false;}
            case 4:
                if(value>MaximumItems)return false;for(ulong i=0;i<value;i++)if(!Item(bytes,ref offset,depth+1,ref items,out _))return false;return true;
            case 5:
                if(value>MaximumItems)return false;ulong prior=0;var hasPrior=false;for(ulong i=0;i<value;i++){var keyOffset=offset;if(keyOffset>=bytes.Length||bytes[keyOffset]>>5!=0||!Item(bytes,ref offset,depth+1,ref items,out var key)||hasPrior&&key<=prior)return false;prior=key;hasPrior=true;if(!Item(bytes,ref offset,depth+1,ref items,out _))return false;}return true;
            case 7:return additional is 20 or 21;
            default:return false;
        }
    }
    private static bool Argument(ReadOnlySpan<byte> bytes,ref int offset,int additional,out ulong value)
    {value=0;if(additional<24){value=(ulong)additional;return true;}var count=additional switch{24=>1,25=>2,26=>4,27=>8,_=>0};if(count==0||offset>bytes.Length-count)return false;value=count switch{1=>bytes[offset],2=>BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]),4=>BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]),_=>BinaryPrimitives.ReadUInt64BigEndian(bytes[offset..])};offset+=count;return true;}
    private static bool Take(ReadOnlySpan<byte> bytes,ref int offset,ulong length)=>Take(bytes,ref offset,length,out _);
    private static bool Take(ReadOnlySpan<byte> bytes,ref int offset,ulong length,out ReadOnlySpan<byte> taken)
    {taken=default;if(length>int.MaxValue||offset>bytes.Length-(int)length)return false;taken=bytes.Slice(offset,(int)length);offset+=(int)length;return true;}
}
