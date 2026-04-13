param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [Parameter(Mandatory = $true)]
    [string]$Output
)

$code = @"
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class TypeLibTool
{
    public enum RegKind
    {
        Default = 0,
        Register = 1,
        None = 2
    }

    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string fileName, RegKind regKind, out ITypeLib typeLib);

    public static void Convert(string source, string output)
    {
        ITypeLib typeLib;
        LoadTypeLibEx(source, RegKind.None, out typeLib);

        var converter = new TypeLibConverter();
        var sink = new ImporterCallback();
        var outputDirectory = System.IO.Path.GetDirectoryName(output);
        var outputFile = System.IO.Path.GetFileName(output);
        var originalDirectory = Environment.CurrentDirectory;

        Environment.CurrentDirectory = outputDirectory;
        try
        {
            AssemblyBuilder builder = converter.ConvertTypeLibToAssembly(
                typeLib,
                outputFile,
                TypeLibImporterFlags.None,
                sink,
                null,
                null,
                null,
                null);
            builder.Save(outputFile);
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    private sealed class ImporterCallback : ITypeLibImporterNotifySink
    {
        public void ReportEvent(ImporterEventKind eventKind, int eventCode, string eventMessage)
        {
            Console.WriteLine(eventMessage);
        }

        public Assembly ResolveRef(object typeLib)
        {
            return null;
        }
    }
}
"@

Add-Type -TypeDefinition $code -Language CSharp
[TypeLibTool]::Convert($Source, $Output)
