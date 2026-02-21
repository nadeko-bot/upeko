using System.Collections.Generic;
using upeko.Models;

namespace upeko.Services
{
    public interface IBotRepository
    {
        bool RecoveredFromBackup { get; }

        /// <summary>
        /// Gets the current configuration
        /// </summary>
        /// <returns>The current configuration</returns>
        ConfigModel GetConfig();
        
        /// <summary>
        /// Gets all bots from the repository
        /// </summary>
        /// <returns>List of bot models</returns>
        List<BotModel> GetBots();
        
        /// <summary>
        /// Adds a new bot to the repository
        /// </summary>
        /// <param name="bot">The bot to add</param>
        void AddBot(BotModel bot);
        
        /// <summary>
        /// Updates an existing bot in the repository
        /// </summary>
        /// <param name="bot">The bot to update</param>
        void UpdateBot(BotModel bot);
        
        /// <summary>
        /// Removes a bot from the repository
        /// </summary>
        /// <param name="bot">The bot to remove</param>
        void RemoveBot(BotModel bot);
        
        /// <summary>
        /// Saves the current configuration to storage
        /// </summary>
        void SaveConfig();
    }
}
