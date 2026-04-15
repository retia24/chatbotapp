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

        [KernelFunction("AnalyzeUserHistory")]
        [Description("Elemzi a felhasználó korábbi beszélgetéseit és létrehoz egy viselkedési és érdeklődési profilt. Csak akkor hívd meg, ha a felhasználó KIFEJEZETTEN arra kér, hogy tudj meg többet róla, vagy elemezd a profilját.")]
        public async Task<string> AnalyzeUserHistoryAsync(Kernel kernel)
        {
            var messages = await _dbService.GetRecentUserMessagesAcrossAllChatsAsync(_userId, 50); 
            string rawData = string.Join("\n", messages.Select(m => m.Content));
            if (string.IsNullOrWhiteSpace(rawData)) return "Nincs elég adat az elemzéshez.";

            var analyzerPrompt = $"Elemezd a következő beszélgetéseket, és írj egy pontos profilt a felhasználóról:\n{rawData}";
            var profileResult = await kernel.InvokePromptAsync(analyzerPrompt);
            return profileResult.ToString();
        }

        [KernelFunction("GenerateTopicRecommendations")]
        [Description("3 témajavaslatot generál a felhasználónak. Csak akkor hívd meg, ha a felhasználó KIFEJEZETTEN KÉRI, hogy ajánlj neki témákat, miről beszélgessetek, vagy adj neki ötleteket a beszélgetésre.")]
        public async Task<string> GenerateRecommendationsAsync(Kernel kernel, [Description("A felhasználó profilja vagy elemzése. Opcionális.")] string userProfile = "")
        {
            if (string.IsNullOrWhiteSpace(userProfile))
            {
               userProfile = await AnalyzeUserHistoryAsync(kernel);
            }
            var strategistPrompt = @"Te egy szigorú adatgeneráló vagy. A következő profil alapján találj ki pontosan 3 izgalmas témát a felhasználónak beszélgetésre.
SZIGORÚ SZABÁLYOK:
- Pontosan 3 témát írj!
- Mindegyik téma pontosan 1 mondat hosszú legyen!
- KIZÁRÓLAG a 3 mondatot írd le, minden mondatot egy új sorba!
- NE HASZNÁLJ sorszámozást vagy kötőjeleket!
- TILOS bármilyen bevezető vagy lezáró szöveget (pl. 'Íme a témák:', 'Ezt a 3 témát találtam neked') belevenni a válaszodba! A válaszod kizárólag a 3 téma szövegéből állhat.

Profil:
" + userProfile;
            var finalRecommendations = await kernel.InvokePromptAsync(strategistPrompt);

            return finalRecommendations.ToString();
        }
    }
}