using System.Diagnostics;
using System.Reflection;

// Harmless fixture. Writes only explicit test files; never installs any software.
if(args.Length==3&&args[0]=="--process-fixture"&&Path.IsPathFullyQualified(args[2]))
{
    if(args[1]=="delayed-write"){await Task.Delay(900);File.WriteAllText(args[2],"child completed");return 0;}
    if(args[1]=="inherited-pipes")
    {
        var child=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))child.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach(var argument in new[]{"--process-fixture","delayed-write",args[2]})child.ArgumentList.Add(argument);
        using var started=Process.Start(child);return started==null?65:0;
    }
    if(args[1]=="large-output"){Console.Write(new string('x',70000));Console.WriteLine("\nThere are no pins configured.");return 0;}
    return 64;
}
if(args.Length!=3||!Path.IsPathFullyQualified(args[0])||!int.TryParse(args[2],out int code))return 64;
if(code==0)File.WriteAllText(args[0],args[1]);
Console.WriteLine(code==0?"Fixture completed":"Fixture simulated failure");
return code;
