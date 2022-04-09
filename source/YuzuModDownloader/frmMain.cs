﻿using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace YuzuModDownloader
{
    public partial class frmMain : Form
    {
        public frmMain()
        {
            InitializeComponent();
        }

        private void frmMain_Load(object sender, EventArgs e)
        {
            // check if software is latest version 
            CheckAppVersion();

            // set UI defaults 
            lblProgress.Text = "";
            chkDeleteModArchives.Checked = true;
            toolTip1.SetToolTip(chkClearModDataLocation, "Deletes all existing mods before downloading the latest Yuzu Game Mods");
            toolTip1.SetToolTip(chkDeleteModArchives, "Deletes all downloaded mod archive files once unpacked");
            toolTip1.SetToolTip(btnDownload, "Download Yuzu Game Mods for current switch games dumped");
        }

        private async void btnDownload_Click(object sender, EventArgs e)
        {
            // disable form controls
            ToggleControls(false);

            // get prerequisites
            string gameTitleIDsXml = "GameTitleIDs.xml";
            var modDownloader = new ModDownloader();
            modDownloader.UpdateProgress += ModDownloader_UpdateProgress;
            await modDownloader.DownloadPrerequisitesAsync();
            await modDownloader.DownloadGameTitleIdDatabaseAsync(gameTitleIDsXml);
            var gameTitleNameIDs = modDownloader.ReadGameTitleIdDatabase(gameTitleIDsXml);

            // download mods for each game 
            int totalGames = 0;
            foreach (var g in gameTitleNameIDs)
            {
                string titleName = g.Key;
                string titleId = g.Value;
                await modDownloader.DownloadTitleModsAsync(titleName, titleId, "https://github.com/yuzu-emu/yuzu/wiki/Switch-Mods", chkClearModDataLocation.Checked);
                totalGames++;
            }

            // tell user mods have been downloaded 
            MessageBox.Show($"Done! Mods downloaded for {totalGames} games.{Environment.NewLine}{Environment.NewLine}" +
                            $"To toggle specific game mods On/Off:{Environment.NewLine}" +
                            $"Run yuzu > Right-Click on a game > Properties > Add-Ons", this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

            // cleanup & reset ui 
            if (chkDeleteModArchives.Checked) modDownloader.DeleteDownloadedModArchives();
            if (File.Exists(gameTitleIDsXml)) File.Delete(gameTitleIDsXml);
            ToggleControls(true);
        }

        private void ModDownloader_UpdateProgress(int progressPercentage, string progressText)
        {
            pbarProgress.Value = progressPercentage;
            pbarProgress.Refresh();
            lblProgress.Text = progressText;
            lblProgress.Refresh();
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void YuzuWebsiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://yuzu-emu.org/");
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var f = new frmAbout())
            {
                f.ShowDialog();
            }
        }

        private void CheckForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CheckAppVersion(true);
        }

        private void ToggleControls(bool value)
        {
            btnDownload.Enabled = value;
            chkClearModDataLocation.Enabled = value;
            chkDeleteModArchives.Enabled = value;
            lblProgress.Text = "";
            pbarProgress.Value = 0;

        }

        private void CheckAppVersion(bool manualCheck = false)
        {
            var currentAppVersion = AppUpdater.CheckVersion();
            switch (currentAppVersion)
            {
                case AppUpdater.CurrentVersion.LatestVersion when manualCheck:
                    MessageBox.Show("You currently have the latest version of Yuzu Mod Downloader", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case AppUpdater.CurrentVersion.UpdateAvailable:
                    MessageBox.Show("New version of Yuzu Mod Downloader available, please download from https://github.com/amakvana/YuzuModDownloader", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Process.Start("https://github.com/amakvana/YuzuModDownloader");
                    break;
                case AppUpdater.CurrentVersion.NotSupported:
                    MessageBox.Show("This version of Yuzu Mod Downloader is no longer supported, please download the latest version from https://github.com/amakvana/YuzuModDownloader", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Process.Start("https://github.com/amakvana/YuzuModDownloader");
                    Application.Exit();
                    break;
            }
        }
    }
}