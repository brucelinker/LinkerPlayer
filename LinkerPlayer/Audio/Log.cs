using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace LinkerPlayer.Audio
{
    namespace Log
    {
        public enum LogInfoType
        {
            Info,
            Warning,
            Error
        }

        public class LogSettings
        {
            private static ILog _selectedLog = null!;

            public static ILog SelectedLog
            {
                get => _selectedLog;
                set => _selectedLog = value ?? throw new ArgumentNullException($"{nameof(Log)} can`t be null");
            }

            static LogSettings()
            {
                SelectedLog = new LogIntoFile();
            }
        }

        public interface ILog
        {
            void Print(string message, LogInfoType logType, [CallerMemberName] string callerName = "");
        }

        public class LogIntoConsole : ILog
        {
            public void Print(string message, LogInfoType logType, [CallerMemberName] string callerName = "")
            {
                Console.WriteLine(message);
            }
        }

        public class LogIntoFile : ILog
        {
            private readonly string _pathToFile;
            private readonly long _fileSizeLimit = 100000; // 100 KB

            public LogIntoFile(string path = "Logs/logs.txt")
            {
                if (path == null || String.IsNullOrWhiteSpace(path))
                {
                    throw new ArgumentNullException(nameof(path));
                }

                _pathToFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LinkerPlayer", path);

                if (!File.Exists(_pathToFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_pathToFile) ?? string.Empty);

                    File.Create(_pathToFile).Close();
                }

                WriteMessageIntoFile("\n===App Starting [" + DateTime.Now + "]===\n");
            }

            public void Print(string message, LogInfoType logType, [CallerMemberName] string callerName = "")
            {
                WriteMessageIntoFile($"[{DateTime.Now}][{logType.ToString()}][{callerName}] " +
                                     message.Replace("\n", ""));
            }

            private void WriteMessageIntoFile(string message)
            {
                if (File.Exists(_pathToFile) && new FileInfo(_pathToFile).Length > _fileSizeLimit)
                {
                    string pathToOldFile = Path.Combine(Path.GetDirectoryName(_pathToFile) ?? string.Empty,
                        Path.GetFileNameWithoutExtension(_pathToFile)) + "_old.txt";

                    if (File.Exists(pathToOldFile))
                    {
                        File.Delete(pathToOldFile);
                    }

                    File.Move(_pathToFile, pathToOldFile);

                    File.Create(_pathToFile).Close();
                }

                using StreamWriter writer = new StreamWriter(_pathToFile, true);
                writer.WriteLine(message);
            }

            public string LogsPath => _pathToFile;
        }
    }
}