using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using FunChatBotApp.Services;

namespace FunChatBotApp.Plugins
{
    public class UserStrategistPlugin
    {
        private readonly CosmosDbService _dbService;
        private readonly string _userId;

        public UserStrategistPlugin(CosmosDbService dbService, string userId)
        {
            _dbService = dbService;
            _userId = userId;
        }

        private async Task<string> ExtractUserDataAsync()
        {
            var messages = await _dbService.GetRecentUserMessagesAcrossAllChatsAsync(_userId, 50); 
            return string.Join("\n", messages.Select(m => m.Content));
        }

        [KernelFunction("GenerateTopicRecommendations")]
        [Description("Generál 3 témajavaslatot a felhasználó korábbi chatjei alapján. Hívd meg, ha a felhasználó témákat, ötleteket vagy miről beszéljünk kérdést tesz fel.")]
        public async Task<string> GenerateRecommendationsAsync(Kernel kernel)
        {
            string rawData = await ExtractUserDataAsync();
            if (string.IsNullOrWhiteSpace(rawData)) return "Nincs elég adat az ajánláshoz.";

            var analyzerPrompt = $"Elemezd a következő beszélgetéseket, és írj egy pontos profilt a felhasználóról:\n{rawData}";
            var profileResult = await kernel.InvokePromptAsync(analyzerPrompt);

            var strategistPrompt = @"Te egy szigorú adatgeneráló vagy. A következő profil alapján találj ki pontosan 3 izgalmas témát a felhasználónak beszélgetésre.
SZIGORÚ SZABÁLYOK:
- Pontosan 3 témát írj!
- Mindegyik téma pontosan 1 mondat hosszú legyen!
- KIZÁRÓLAG a 3 mondatot írd le, minden mondatot egy új sorba!
- NE HASZNÁLJ sorszámozást vagy kötőjeleket!
- TILOS bármilyen bevezető vagy lezáró szöveget (pl. 'Íme a témák:', 'Ezt a 3 témát találtam neked') belevenni a válaszodba! A válaszod kizárólag a 3 téma szövegéből állhat.

Profil:
" + profileResult;
            var finalRecommendations = await kernel.InvokePromptAsync(strategistPrompt);

            return finalRecommendations.ToString();
        }
    }
}