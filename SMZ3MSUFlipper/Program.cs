// See https://aka.ms/new-console-template for more information


using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MSURandomizerLibrary;
using MSURandomizerLibrary.Configs;
using MSURandomizerLibrary.Models;
using MSURandomizerLibrary.Services;
using Serilog;

public static class Program
{
    private static ServiceProvider _services = null!;

    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Debug()
            .CreateLogger();
        
        if (!Directory.Exists(MainPath))
        {
            Directory.CreateDirectory(MainPath);
        }
        
        _services = new ServiceCollection()
            .AddLogging(logging =>
            {
                logging.AddSerilog(dispose: true);
            })
            .AddMsuRandomizerServices()
            .BuildServiceProvider();

        _services.GetRequiredService<IMsuRandomizerInitializationService>().Initialize(
            new MsuRandomizerInitializationRequest()
            {
                MsuAppSettingsStream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("SMZ3MSUFlipper.msu-randomizer-settings.yml"),
                LookupMsus = false
            });

        Console.Write("Provide an MSU: ");
        var msuPath = Console.ReadLine();

        if (string.IsNullOrEmpty(msuPath))
        {
            return;
        }

        if (msuPath.EndsWith(".msu", StringComparison.OrdinalIgnoreCase))
        {
            msuPath = Path.GetDirectoryName(msuPath);
        }

        var msus = _services.GetRequiredService<IMsuLookupService>().LookupMsus(msuPath, ignoreCache: true);
        var currentType = _services.GetRequiredService<IMsuTypeService>().GetSMZ3MsuType();
        var legacyType = _services.GetRequiredService<IMsuTypeService>().GetSMZ3LegacyMSUType();

        foreach (var msu in msus.Where(x => x.MsuType == legacyType))
        {
            ConvertMsu(msu, currentType!);
        }
    }

    private static void ConvertMsu(Msu msu, MsuType targetType)
    {
        Log.Information("MSU: {Name} ({Type})", msu.Name, msu.MsuTypeName);
        
        var conversion = targetType.Conversions[msu.MsuType!];

        var msuPath = new FileInfo(msu.Path);
        var msuDirectory = msuPath.DirectoryName!;

        var newDirectory = Path.Combine(msuDirectory, "flipped");
        if (Directory.Exists(newDirectory))
        {
            Directory.Delete(newDirectory, true);
        }

        Directory.CreateDirectory(newDirectory);
        var baseName = Path.GetFileNameWithoutExtension(msuPath.Name);
        var newPath = Path.Combine(newDirectory, msuPath.Name);
        
        Log.Information("Creating new MSU: {Path}", newPath);
        
        File.Copy(msu.Path, newPath);
        
        HashSet<string> swappedFiles = new HashSet<string>();
        var newTracks = new List<Track>();

        foreach (var oldTrack in msu.Tracks)
        {
            if (oldTrack.IsCopied || !File.Exists(oldTrack.Path)) continue;
            var oldTrackNumber = oldTrack.Number;
            var newTrackNumber = conversion(oldTrackNumber);
            
            // Log.Information("{Old} => {New}", oldTrackNumber, newTrackNumber);

            var newPcmName = Path.GetFileNameWithoutExtension(oldTrack.Path).Replace($"{baseName}-{oldTrackNumber}", $"{baseName}-{newTrackNumber}");
            var newPcmPath = Path.Combine(newDirectory, $"{newPcmName}.pcm");
            
            Log.Information("{Old} => {New}", oldTrack.Path, newPcmPath);
            
            File.Copy(oldTrack.Path, newPcmPath);

            oldTrack.Number = newTrackNumber;
            oldTrack.Path = newPcmPath;
            newTracks.Add(oldTrack);
        }

        msu.Tracks = newTracks;
        msu.Path = newPath;
        msu.MsuType = targetType;
        var msuDetailsService = _services.GetRequiredService<IMsuDetailsService>();
        msuDetailsService.SaveMsuDetails(msu, newPath.Replace(".msu", ".yml"), out var error);

        if (!string.IsNullOrEmpty(error))
        {
            Log.Error("Error creating MSU {Path}: {Message}", newPath, error);
        }
        else
        {
            Log.Information("Successfully created MSU {Path}", newPath);
        }

    }
    
    private static string MainPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMZ3MSUFlipper");
}