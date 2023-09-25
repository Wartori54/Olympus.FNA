
using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;
using System;
using System.Globalization;
using System.IO;

namespace Olympus; 

public class AppLogger {

    public static readonly string DateFormat = "yyyyMMdd-hhmmss";
    public static readonly string PathDir = Path.Combine(Config.GetDefaultDir(), "logs");

    public static ILogger Log {
        get {
            if (Instance == null) 
                throw new InvalidOperationException("Attempt to access the Logger before its creation!");
            
            return Instance.logger;
        }
    }

    public static AppLogger? Instance { get; private set; }
    private readonly ILogger logger;

    public static void Create() {
        if (Instance != null)
            throw new InvalidOperationException("AppLogger created multiple times!");
        Instance = new AppLogger();
    }

    private AppLogger() {
        if (!Directory.Exists(PathDir)) Directory.CreateDirectory(PathDir);
        // Keep 5 logs, counting the one we're going to create now
        while (Directory.GetFiles(PathDir).Length > 5-1) {
            DeleteOldest(PathDir);
        }

        logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(PathDir, $"log-{DateTime.Now.ToString(DateFormat)}.txt"), 
                flushToDiskInterval: TimeSpan.FromSeconds(30),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
            #if DEBUG
            .MinimumLevel.Debug()
            #else
            .MinimumLevel.Information()
            #endif
            .CreateLogger();
    }


    private void DeleteOldest(string dir) {
        string oldest = "";
        DateTime oldestDT = DateTime.Now;
        string prefix = "log-";
        foreach (string logPath in Directory.EnumerateFiles(dir)) {
            string log = Path.GetFileName(logPath);
            string strDate = log.Substring(prefix.Length, DateFormat.Length);
            DateTime dateTime = DateTime.ParseExact(strDate, DateFormat, CultureInfo.InvariantCulture);
            
            if (dateTime.CompareTo(oldestDT) < 0) {
                oldest = logPath;
                oldestDT = dateTime;
            }
        }

        if (oldest == "") return;
        File.Delete(oldest);
    }
}