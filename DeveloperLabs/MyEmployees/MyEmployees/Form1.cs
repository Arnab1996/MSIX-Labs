﻿using Microsoft.Win32;
using MyEmployees.Entities;
using MyEmployees.Helpers;
using MyEmployees.PluginInterface;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.ApplicationModel;
using Windows.Storage;

namespace ExportDataLibrary
{
    public partial class Form1 : Form
    {
        Config config;
        IPlugin plugin;
        Logger logger;
        // Stores the path of a local file containing the new package
        public static readonly string inputPackageUri = "c:\\temp\\MyEmployees.Package.msixbundle";
        // Stores the path of a local file containing the version data of the new package
        public static readonly string inputPackageVersionUri = "c:\\temp\\version.txt";
        static readonly int imgColumn = 1;
        static int rowClicked = 0;
        StorageFile imgFile = null;

        public Form1()
        {
            InitializeComponent();
            var logManager = LogManager.LoadConfiguration("NLog.config");
            logger = logManager.GetCurrentClassLogger();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            LoadConfig();
            LoadData();
            CheckKioskMode();
            Scenarios.InitiateBackgroundCheck();
            //await CheckForUpdates();
        }

        private async Task CheckForUpdates()
        {
            var result = await Package.Current.CheckUpdateAvailabilityAsync();
            if (result.Availability == PackageUpdateAvailability.Available)
            {
                MessageBox.Show("There's a new update! Restart your app to install it");
            }
        }

        private void CheckKioskMode()
        {
            var regKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Contoso\\MyEmployees");
            if (regKey != null)
            {
                var kioskMode = regKey.GetValue("KioskMode");
                if (kioskMode != null)
                {
                    string isKioskModeEnabled = kioskMode.ToString().ToLowerInvariant();
                    if (isKioskModeEnabled == "true")
                    {
                        menuStrip1.Visible = false;
                        logger.Log(LogLevel.Info, "Kiosk mode enabled");
                    }
                }
            }
        }

        private void LoadConfig()
        {
            string path = $"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}\\Contoso\\MyEmployees\\config.json";
            if (File.Exists(path))
            {
                logger.Log(LogLevel.Info, "Custom config file is available");
                string json = File.ReadAllText(path);
                config = JsonConvert.DeserializeObject<Config>(json);

                if (!config.IsCheckForUpdatesEnabled)
                {
                    logger.Log(LogLevel.Info, "Check for updates disabled");
                }
            }
            else
            {
                logger.Log(LogLevel.Info, "Custom config file isn't available");
            }
            try
            {
                string dllPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}\\Contoso\\MyEmployees\\Plugins\\ExportDataLibrary.dll";
                plugin = LoadAssembly(dllPath);
                logger.Log(LogLevel.Info, "Export data plugin available");
            }
            catch (Exception)
            {
                logger.Log(LogLevel.Info, "Export data plugin isn't available");
            }
        }

        private void LoadData()
        {
            string result = Assembly.GetExecutingAssembly().Location;
            int index = result.LastIndexOf("\\");
            string dbPath = $"{result.Substring(0, index)}\\Employees.db";

            SQLiteConnection connection = new SQLiteConnection($"Data Source= {dbPath}");
            using (SQLiteCommand command = new SQLiteCommand(connection))
            {
                connection.Open();
                command.CommandText = "SELECT * FROM Employees";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Employee employee = new Employee
                        {
                            EmployeeId = int.Parse(reader[0].ToString()),
                            FirstName = reader[1].ToString(),
                            LastName = reader[2].ToString(),
                            Email = reader[3].ToString()
                        };

                        employeeBindingSource.Add(employee);
                    }
                }
            }
            dataGridView.DataSource = employeeBindingSource;
            LoadNewEmployees();
            LoadEmployeePictures();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutForm aboutForm;
            if (config != null)
            {
                aboutForm = new AboutForm(config.About.CompanyName, config.About.SupportLink, config.About.SupportMail);
            }
            else
            {
                aboutForm = new AboutForm();
            }

            aboutForm.ShowDialog();
        }

        private void exportAsCSVToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "CSV|*.csv";
            saveFileDialog.Title = "Save a CSV file";
            saveFileDialog.ShowDialog();

            bool isFileSaved = plugin.Execute(employeeBindingSource.List, saveFileDialog.FileName);
            if (isFileSaved)
            {
                MessageBox.Show("The CSV file has been exported with success");
            }
            else
            {
                MessageBox.Show("The export operation has failed");
            }
        }

        private IPlugin LoadAssembly(string assemblyPath)
        {
            string assembly = Path.GetFullPath(assemblyPath);
            Assembly ptrAssembly = Assembly.LoadFile(assembly);
            foreach (Type item in ptrAssembly.GetTypes())
            {
                if (!item.IsClass) continue;
                if (item.GetInterfaces().Contains(typeof(IPlugin)))
                {
                    return (IPlugin)Activator.CreateInstance(item);
                }
            }
            throw new Exception("Invalid DLL, Interface not found!");
        }

        public void LoadNewEmployees()
        {
            try
            {
                string path = ApplicationData.Current.LocalFolder.Path + "\\Downloadtemp.CSV";
                var file = File.OpenText(path);
                var reader = new CsvHelper.CsvReader(file);
                while (reader.Read())
                {
                    Employee employee = new Employee
                    {
                        EmployeeId = int.Parse(reader[0].ToString()),
                        FirstName = reader[1].ToString(),
                        LastName = reader[2].ToString(),
                        Email = reader[3].ToString()
                    };
                    employeeBindingSource.Add(employee);
                }
                dataGridView.DataSource = employeeBindingSource;
                file.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Scenarios.InitiateAppUpdate();
        }

        private void dataGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            rowClicked = e.RowIndex;
            // Checks if the cell clicked is an image cell
            if (e.ColumnIndex == imgColumn)
            {
                this.contextMenuStrip.Show(Cursor.Position);
            }
        }

        private async void UploadAndSaveImageAsync()
        {
            String employeeId = rowClicked.ToString();
            var localFolder = ApplicationData.Current.LocalFolder;
            try
            {
                StorageFile file = await imgFile.CopyAsync(localFolder, employeeId + ".jpg", NameCollisionOption.ReplaceExisting);
                dataGridView.Rows[rowClicked].Cells[imgColumn].Value = Image.FromFile(file.Path);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        public async void LoadEmployeePictures()
        {
            IReadOnlyList<StorageFile> files = await ApplicationData.Current.LocalFolder.GetFilesAsync();
            foreach (StorageFile file in files)
            {
                String[] fileName = file.Name.Split('.');
                int id;
                if (int.TryParse(fileName[0], out id))
                {
                    dataGridView.Rows[id].Cells[imgColumn].Value = Image.FromFile(file.Path);
                }
            }
        }

        private async void toolStripUploadNewPicture_Click(object sender, EventArgs e)
        {
            imgFile = await Scenarios.PickFileAsync();
            if (imgFile != null)
            {
                UploadAndSaveImageAsync();
            }
        }
    }
}
