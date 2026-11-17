namespace UpdateDeck.Core;

public static class OperationValidation
{
    public static bool Readable(UpdateOperation? operation,string storedId)
    {
        if(operation==null||!SafeStoredId(operation.Id)||operation.Id!=storedId||!Enum.IsDefined(operation.State))return false;
        if(operation.ActiveProcessId is <=0||operation.ActiveProcessStartedUtcTicks is <0 or >3155378975999999999)return false;
        return Bounded(operation.Id,256)&&Bounded(operation.AppId,256)&&Bounded(operation.Name,4096)&&Bounded(operation.Provider,256)&&
            Bounded(operation.PreviousVersion,4096)&&Bounded(operation.TargetVersion,4096)&&Bounded(operation.Message,65536);
    }
    public static bool SafeStoredId(string? id)=>!string.IsNullOrWhiteSpace(id)&&Bounded(id,256);
    private static bool Bounded(string? text,int maximum)=>text!=null&&text.Length<=maximum&&text.IndexOf('\0')<0;
}
