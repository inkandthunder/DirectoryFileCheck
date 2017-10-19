using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Configuration;
using System.Collections.Specialized;
using System.Net.Mail;
using log4net;

namespace Util
{
    class DirectoryMonitor
    {
        //Update the App.config with variables to be used in your environment
        public static NameValueCollection AppSettings { get; }
        public static DirectoryInfo DirectoryPath = new DirectoryInfo(ReadSetting("readpath"));
        public static List<string> StalledFiles = new List<string> { };
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                //CheckDirectoryPath(DirectoryPath);
                log.Info("Checking for stalled files...");
                if (CheckForFiles() != false & CheckDirectoryPath(DirectoryPath) != false)
                {
                    log.Info("Files found in target directory");
                    CheckAge();
                    if (StalledFiles.Count > 0)
                    {
                        BuildEmailMessage();
                    }
                    else
                    {
                        log.Info("Files were found in target directory, but haven't aged to met the criteria for alerting");
                    }
                }
                else
                {
                    log.Debug("No files found in the directory");
                }
                log.Info("File Check Completed!");
            }
            else {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-watch")
                    {
                        FileWatcher();
                    }
                }
            }

        }

        public static bool CheckDirectoryPath(DirectoryInfo dir)
        {
            try
            {
                if (dir.Exists)
                {
                    log.Info(dir.ToString() + " is a valid path");
                    return true;
                }
                else
                {
                    log.Warn(dir.ToString() + " is not valid or was not found");
                    return false;
                }
            }
            catch(Exception ex)
            {
                log.Error("Invalid Directory Path or Path does not exist", ex);
                return false;
            }
        }

        static string ReadSetting(string key)
        {
            try
            {
                var appSettings = ConfigurationManager.AppSettings;
                return appSettings[key] ?? "Not Found";
            }
            catch (ConfigurationErrorsException ex)
            {
                log.Error("Error reading the app configuration", ex);
                return null;
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return null;
            }
        }

        public static bool CheckForFiles()
        {
            try
            {
                log.Info("Checking " + DirectoryPath + " for files");
                return DirectoryPath.GetFiles().Any();
            }
            catch(Exception ex)
            {
                log.Error("Target directory is not available", ex);
                return false;
            }
        }

        public static void CheckAge()
        {
            string[] allfiles = Directory.GetFiles(DirectoryPath.ToString(), "*.*");
            try
            {
                log.Info("Checking age of files found");
                foreach (var file in allfiles)
                {
                    FileInfo info = new FileInfo(file);
                    bool oldEnough = IsBelowThreshold(file, new TimeSpan(0, int.Parse(ReadSetting("age")), 0, 0));
                    if (oldEnough == true)
                    {
                        log.Info("File '" + info + "' has aged longer than expected");
                        StalledFiles.Add(info.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private static bool IsBelowThreshold(string fileName, TimeSpan thresholdAge)
        {
            log.Debug("Threshold: " + thresholdAge);
            log.Debug("Elapsed Age: " + (DateTime.Now - File.GetCreationTime(fileName)));
            return (DateTime.Now - File.GetCreationTime(fileName)) > thresholdAge;
        }

        public static void BuildEmailMessage()
        {
            var mail = new MailMessage();
            var smtp = new SmtpClient();
            var body = "<p>" + ReadSetting("body") + "<br><br>";
            foreach (var result in StalledFiles)
            {
                body += Path.GetFileName(result) + " : Created on " + File.GetCreationTime(result) + "<br>";
            }
            body += "</p>";

            try
            {
                foreach (var to in ReadSetting("to").Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    mail.To.Add(to);
                }
                foreach (var cc in ReadSetting("cc").Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    mail.CC.Add(cc);
                }
            }
            catch (Exception ex)
            {
                log.Error("Missing email parameters in the app config", ex);
            }

            try
            {
                log.Info("Sending notification to distribution list");
                mail.From = new MailAddress(ReadSetting("from"));
                mail.Subject = ReadSetting("subject");
                mail.IsBodyHtml = true;
                mail.Body = string.Format(body);
                smtp.Port = int.Parse(ReadSetting("port"));
                smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                smtp.UseDefaultCredentials = false;
                smtp.Host = ReadSetting("smtpHost");
                SendEmail(smtp, mail);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                log.Error("Email could not be delivered", ex);
            }
        }

        private static void SendEmail(SmtpClient smtp, MailMessage mail)
        {
            try
            {
                smtp.Send(mail);
                mail.Dispose();
            }
            catch (SmtpFailedRecipientsException ex)
            {
                log.Error("There was a problem sending the email", ex);
                mail.Dispose();
            }
            //mail.Dispose();
        }

        private static void FileWatcher()
        {
            log.Info("Starting Directory Watcher in " + DirectoryPath.ToString());
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = DirectoryPath.ToString();
            watcher.Created += watcher_Created;
            watcher.Deleted += watcher_Deleted;
            watcher.Renamed += Watcher_Renamed;
            watcher.EnableRaisingEvents = true;
            new System.Threading.AutoResetEvent(false).WaitOne();
        }

        private static void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            log.Info("File Renamed: " + e.OldName + " -> " + e.Name);
        }

        static void watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            log.Info("File Deleted: " + e.Name);
        }

        static void watcher_Created(object sender, FileSystemEventArgs e)
        {
            log.Info("File Created: " + e.Name);
        }
    }
}
