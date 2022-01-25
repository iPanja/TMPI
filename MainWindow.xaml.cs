using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace TMPI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private String modpackLocation = "";
        private String forgeLocation = "";

        public MainWindow() {
            InitializeComponent();
        }

        private void onSelectModpackClick(object sender, RoutedEventArgs e) {
            String filename = selectFile("ZIP file (*.zip)|*.zip");
            System.Console.WriteLine($"Modpack selected: {filename}");
            if (!filename.Equals(String.Empty)) {
                modpackLabel.Content = filename;
                modpackLocation = filename;
            }
        }

        private void onSelectForgeClick(object sender, RoutedEventArgs e) {
            String filename = selectFile("JAR file(*.jar)|*.jar");
            System.Console.WriteLine($"Forge selected: {filename}");
            if (!filename.Equals(String.Empty)) {
                forgeLabel.Content = filename;
                forgeLocation = filename;
            }
        }

        private String selectFile(String filter) {
            OpenFileDialog opd = new OpenFileDialog();
            opd.Filter = filter;
            opd.FilterIndex = 0;
            opd.Multiselect = false;

            //Nullable<bool> result = opd.ShowDialog();

            if(opd.ShowDialog() ?? false) { //Converts null state -> false, otherwise convert to its original value
                return opd.FileName;
            }
            return String.Empty;
        }

        private void onInstallClick(object sender, RoutedEventArgs e) {
            ((Button)sender).IsEnabled = false;
            logTextBox.Text = "";

            if (modpackLocation.Equals(String.Empty) || forgeLocation.Equals(String.Empty)) {
                cancelInstallation("Specify the modpack/forge locations first!");
                return;
            }

            String modpackName = modpackNameTextBox.Text;
            String folderSafeName = getFolderSafeName(modpackName);
            Log($"Installing modpack {modpackName} under the directory {folderSafeName}");

            Log("Locating technic installation...");
            String technicRoot = getTechnicRootFolder();
            if (technicRoot.Equals(String.Empty)) {
                cancelInstallation("Failed to find technic installation folder (.technic)");
                return;
            } else {
                Log($"Found technic installation folder: {technicRoot}");
            }

            Log("Locating installedPack file...");
            if (!File.Exists(Path.Combine(technicRoot, @"installedPacks"))) {
                cancelInstallation("installedPack file NOT found");
                return;
            } else {
                Log($"Found installedPack file");
            }

            Log("Attempting to add modpack entry to installedPack file...");
            addModpackEntry(Path.Combine(technicRoot, @"installedPacks"), modpackName, folderSafeName);
            Log("Added modpack entry");

            Log("Attempting to extract modpack...");
            ExtractModpack(Path.Combine(technicRoot, $@"modpacks/{folderSafeName}"), modpackLocation);

            Log("Locating minecraft version...");
            String version = ScrapeVersion(Path.Combine(technicRoot, $@"modpacks/{folderSafeName}/bin/"));
            if (version.Equals(String.Empty)) {
                Log("Failed to find minecraft version (not-fatal)");
            } else {
                Log($"Running minecraft version {version}");
            }

            Log("Checking for forge installation");
            CheckForge(Path.Combine(technicRoot, $@"modpacks/{folderSafeName}"), forgeLocation);


            ((Button)sender).IsEnabled = true;
        }

        private void cancelInstallation(String error) {
            Log(error);
            installButton.IsEnabled = true;
        }

        private void Log(String message) {
            logTextBox.AppendText(message + "\n");
        }

        private String getFolderSafeName(String file) {
            Array.ForEach(Path.GetInvalidFileNameChars(),
                  c => file = file.Replace(c.ToString(), String.Empty));
            return file;
        }

        private String getTechnicRootFolder() {
            String location = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @".technic\");
            if (Directory.Exists(location)) {
                return location;
            } else {
                return "";
            }
            //var fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DateLinks.xml");
        }

        private void addModpackEntry(String fileLocation, String modpackName, String folderName) {
            //TODO: Catch an error when the json file already contains an entry for a modpack with the same name
            string json = File.ReadAllText(fileLocation);
            dynamic jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

            String raw = $"{{ \"name\": \"{modpackName}\", \"build\": \"recommended\", \"directory\": \"%MODPACKS%\\\\{folderName}\" }}";
            jsonObj["installedPacks"].Add(new JProperty(modpackName, JObject.Parse(raw)));

            File.WriteAllText(fileLocation, Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj));
        }

        private void ExtractModpack(String modpackFolderLocation, String modpackZipLocation) {
            //Check for an extra embedded folder
            using(ZipArchive archive = ZipFile.Open(modpackZipLocation, ZipArchiveMode.Read)) {
                if(archive.GetEntry("bin/") == null) {
                    //Find the base folder, in case it was nested
                    Log("Scanning for root mod folder [Nested Installation]");
                    String root = "";
                    foreach(ZipArchiveEntry entry in archive.Entries) {
                        if (entry.FullName.Contains("/bin/")) {
                            Console.WriteLine(entry.FullName);
                            //Extract all files in subfolder
                            root = entry.FullName.Replace("bin/", "");
                            break;
                        }
                    }
                    Log($"Found subdirectory: {root}");
                    Log($"Extracting files... ");
                    //Extract files in the nested "base folder"
                    Directory.CreateDirectory(modpackFolderLocation);
                    foreach(ZipArchiveEntry entry in archive.Entries) {
                        if (entry.FullName.StartsWith(root)) {
                            String destination = Path.Combine(modpackFolderLocation, entry.FullName.Replace(root, ""));
                            
                            if (String.IsNullOrEmpty(entry.Name)) {
                                Directory.CreateDirectory(destination);
                            } else {
                                entry.ExtractToFile(destination);
                            }
                        }
                    }
                } else {
                    //Extract the entire zip file, normal process if the modpack was compiled correctly
                    Log("Extracting modpack [Normal installation]");
                    archive.ExtractToDirectory(modpackFolderLocation);
                }
                Log("Finished extracting!");
            }
        }

        private void CheckForge(String modpackLocation, String forgeLocation) {
            string[] binfiles = Directory.GetFiles(Path.Combine(modpackLocation, "bin/"), "*.jar");
            //Attempt to locate the necessary JAR files: minecraft, and forge (modpack)
            bool mcFound = false;
            bool forgeFound = false;
            foreach(string file in binfiles) {
                if (file.Contains("minecraft.jar")) {
                    mcFound = true;
                }else if(file.Contains("modpack.jar")) {
                    forgeFound = true;
                }
            }

            //Install forge version if necessary
            if (!forgeFound) {
                Log("Forge not found, installing forge...");
                File.Copy(forgeLocation, Path.Combine(modpackLocation, "bin/modpack.jar"), false);
                Log("Forge installed");
            }
        }

        private String ScrapeVersion(String binFolder) {
            string[] binfiles = Directory.GetFiles(binFolder, "*.json");
            foreach (string f in binfiles) {
                if (f.Contains("version.json")) {
                    string json = File.ReadAllText(Path.Combine(binFolder, "version.json"));
                    dynamic jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    return jsonObj["jar"];
                }
            }
            return String.Empty;
        }
    }
}
