using System;
using System.Collections.Generic;
using System.IO;

namespace upeko.Models
{
    public class ConfigModel
    {
        public List<BotModel> Bots { get; set; } = new List<BotModel>();

        public string DefaultBotsFolder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "upeko",
            "bots");

        public bool MinimizeToTray { get; set; } = true;

        public string Language { get; set; } = "en-US";

        public bool IsDarkTheme { get; set; } = false;

        /// <summary>
        /// The newest NadekoBot release whose notes the user opened, or whose release strip the user closed.
        /// </summary>
        public string? SeenBotRelease { get; set; }

        /// <summary>
        /// The newest NadekoBot release that Upeko found, so the release strip can show at startup before the check ends.
        /// </summary>
        public string? LatestBotRelease { get; set; }
    }
}