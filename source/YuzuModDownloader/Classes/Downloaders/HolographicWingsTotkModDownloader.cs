﻿using System.Diagnostics;
using System.Text.Json;
using System.Xml;
using YuzuModDownloader.Classes.Entities;
using YuzuModDownloader.Classes.Utilities;

namespace YuzuModDownloader.Classes.Downloaders
{
    public class HolographicWingsTotkModDownloader : ModDownloader
    {
        private const string HolographicWingsTotkXml = "holographicwings.xml";

        private readonly IHttpClientFactory _clientFactory;

        public HolographicWingsTotkModDownloader(IHttpClientFactory clientFactory, bool isModDataLocationToBeDeleted, bool isDownloadedModArchivesToBeDeleted)
            : base(clientFactory, isModDataLocationToBeDeleted, isDownloadedModArchivesToBeDeleted)
        {
            _clientFactory = clientFactory;
        }

        public new async Task DownloadPrerequisitesAsync()
        {
            await base.DownloadPrerequisitesAsync();
            await base.DownloadGameDatabaseAsync($"assets/{HolographicWingsTotkXml}");
        }

        public async Task<List<Game>> ReadGameTitlesDatabaseAsync()
        {
            // detect yuzu user directory 
            // loop through {ModDirPath} folder & get title names from title Id's
            var games = new List<Game>();
            base.RaiseUpdateProgress(0, "Scanning Games Library ...");
            using (var reader = XmlReader.Create(HolographicWingsTotkXml, new XmlReaderSettings
            {
                Async = true,
                IgnoreComments = true
            }))
            {
                while (await reader.ReadAsync())
                {
                    if (!reader.IsStartElement())
                        continue;

                    switch (reader.Name)
                    {
                        case "title_id":
                            string titleId = await reader.ReadElementContentAsStringAsync();
                            await reader.ReadAsync();

                            if (string.IsNullOrWhiteSpace(titleId) || !Directory.Exists(Path.Combine(base.ModDirectoryPath, titleId)))
                                break;

                            string titleVersion = await GetTitleVersion(titleId);
                            games.Add(new Game
                            {
                                TitleID = titleId,
                                ModDataLocation = Path.Combine(base.ModDirectoryPath, titleId),
                                TitleVersion = titleVersion,
                                ModDownloadUrls = await GetModDownloadUrls(titleVersion)   // detect urls for each game and populate the downloads 
                            });
                            break;

                        default: break;     // do nothing 
                    }
                }
            }
            base.RaiseUpdateProgress(100, "Scanning Games Library ...");
            return games;
        }

        public new async Task DownloadModsAsync(List<Game> games)
        {
            // download and unpack the collection package
            await base.DownloadModsAsync(games);

            // go through the entire package and extract the mods only applicable for the current version detected 
            foreach (var game in games)
            {
                await ProcessExefMods(game);
                await ProcessNonExefMods(game);

                // clean up temp folders 
                try
                {
                    Directory.Delete(Path.Combine(game.ModDataLocation, "Mods"), true);
                    Directory.Delete(Path.Combine(game.ModDataLocation, "Guide"), true);
                } catch { }
            }
            CleanUp();
        }

        private static async Task ProcessExefMods(Game game)
        {
            // process all exefs first 
            string[] allExefs = Directory.GetFiles(Path.Combine(game.ModDataLocation, "Mods"), $"*{game.TitleVersion}*.*", SearchOption.AllDirectories);
            foreach (string exef in allExefs)
            {
                // if current mod is part of the "Lazy Packs", ignore it and move on
                // "Lazy Packs" are to be treated as NonExefMods
                if (exef.Contains("Lazy Packs", StringComparison.OrdinalIgnoreCase))
                    continue;

                // extract path to comply with yuzu mod requirements 
                // /load/XXXXXXXXXXXXXXXX/<mod-name>/exefs or romfs/<files>
                var di = new DirectoryInfo(Path.GetDirectoryName(exef)!);
                string dirtyPath = di.Parent?.Parent?.ToString()!;
                string cleanedExefFilePath = Path.Combine(game.ModDataLocation, exef.Replace(dirtyPath, "")[1..]);

                // create directory paths & copy exefs
                Directory.CreateDirectory(Path.GetDirectoryName(cleanedExefFilePath)!);
                await DirectoryUtilities.CopySingleFileAsync(exef, cleanedExefFilePath);

                // delete original so when we process remnants, we don't get duplicated files 
                try
                {
                    Directory.Delete(di.Parent!.ToString(), true);
                }
                catch { }
            }
        }

        private static async Task ProcessNonExefMods(Game game)
        {
            // now process any remnants which are compatible, via "compatible versions.txt"

            // get all "Compatible Versions.txt" files to process 

            string[] allCompatibleVersionsTxtFiles = Directory.GetFiles(Path.Combine(game.ModDataLocation, "Mods"), "Compatible versions.txt", SearchOption.AllDirectories);
            foreach (string compatibleVersionFile in allCompatibleVersionsTxtFiles)
            {
                // if mod not compatible with game version, ignore and move on
                if (!await IsNonExefModCompatibleWithGameVersion(game.TitleVersion, compatibleVersionFile))
                    continue;

                // mod is compatible with game version so process it 

                // extract path to comply with yuzu mod requirements 
                // /load/XXXXXXXXXXXXXXXX/<mod-name>/exefs or romfs/<files>
                var di = new DirectoryInfo(Path.GetDirectoryName(compatibleVersionFile)!);
                string dirtyPath = di.Parent?.ToString()!;
                string cleanedFilePath = Path.Combine(game.ModDataLocation, compatibleVersionFile.Replace(dirtyPath, "")[1..]);

                DirectoryUtilities.CopyAllFiles(di.ToString(), Path.GetDirectoryName(cleanedFilePath)!, true);
            }

            static async Task<bool> IsNonExefModCompatibleWithGameVersion(string gameTitleVersion, string compatibleVersionFile)
            {
                // read in compatibleversion file 
                // parse it so we read the line below
                // X.X.X : Yes/No
                using var reader = new StreamReader(compatibleVersionFile);
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    if (!line.Contains(gameTitleVersion, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // current line contains our version 
                    // return true/false based on if the line also contains yes
                    return line.Contains("Yes", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            }
        }

        /// <summary>
        /// Gets the Title Version information from /cache/game_list/
        /// </summary>
        /// <param name="titleId">The TitleID of the current game</param>
        /// <returns>Title Version if exists, otherise returns 1.0.0</returns>
        private async Task<string> GetTitleVersion(string titleId)
        {
            string pv = Path.Combine(base.UserDirPath, "cache", "game_list", $"{titleId}.pv.txt");
            string defaultVersion = "1.0.0";

            // if <title_id>.pv.txt doesn't exist, return default version 
            if (!File.Exists(pv))
                return defaultVersion;

            // otherwise read in the <title_id>.pv.txt
            // scan for "Update (X.X.X)" line, parse and return X.X.X
            using (var reader = new StreamReader(pv))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    if (!line.StartsWith("Update (", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // extract version from line containing Update (X.X.X)
                    int from = line.IndexOf("(") + 1;
                    int to = line.LastIndexOf(")");
                    return line[from..to];     // extract and return X.X.X 
                }
            }

            return defaultVersion;     // fallback
        }

        /// <summary>
        /// Retrieves all of the download URLs for a specific title.
        /// </summary>
        /// <returns>List of Uri's containing the Urls to Mods.</returns>
        private async Task<List<Uri>> GetModDownloadUrls(string titleVersion)
        {
            // pull releases from github json 
            using var client = _clientFactory.CreateClient("GitHub-Api");
            using var json = await client.GetStreamAsync("repos/HolographicWings/TOTK-Mods-collection/releases/latest");
            var repoData = await JsonSerializer.DeserializeAsync<Repo>(json)!;

            // if no data is fetched, return empty list 
            if (repoData is null)
                return new List<Uri>();

            // for each asset, grab the download url and return it 
            var downloadUrls = new List<Uri>();
            foreach (var asset in repoData.Assets!)
            {
                // if package isn't for current version, ignore 
                if (!asset.BrowserDownloadUrl!.Contains(titleVersion) || !asset.BrowserDownloadUrl!.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                    continue;

                downloadUrls.Add(new Uri(asset.BrowserDownloadUrl));
            }
            return downloadUrls;
        }

        private static void CleanUp()
        {
            // delete xml
            if (File.Exists(HolographicWingsTotkXml))
                File.Delete(HolographicWingsTotkXml);
        }
    }
}
