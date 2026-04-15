#pragma warning disable SKEXP0110 // Elrejti az Agents API kísérleti (Experimental) figyelmeztetéseit

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using FunChatBotApp.Services;

namespace FunChatBotApp.Plugins
{
    // Belső plugin, amit CSAK az adatgyűjtő ágens (DataGatherer) fog látni
    public class DataGathererPlugin
    {
        private readonly CosmosDbService _dbService;
        private readonly string _userId;

        public DataGathererPlugin(CosmosDbService dbService, string userId)
        {
            _dbService = dbService;
            _userId = userId;
        }

        [KernelFunction("GetRecentUserMessages")]
        [Description("Visszaadja a felhasználó utolsó 50 elküldött üzenetét adatelemzés céljából.")]
        public async Task<string> GetRecentUserMessagesAsync()
        {
            var messages = await _dbService.GetRecentUserMessagesAcrossAllChatsAsync(_userId, 50); 
            string rawData = string.Join("\n", messages.Select(m => m.Content));
            return string.IsNullOrWhiteSpace(rawData) ? "Nincs elég adat az elemzéshez." : rawData;
        }
    }

    public class UserStrategistPlugin
    {
        private readonly CosmosDbService _dbService;
        private readonly string _userId;

        public UserStrategistPlugin(CosmosDbService dbService, string userId)
        {
            _dbService = dbService;
            _userId = userId;
        }

        // Fő delegáló függvény: A karmester MESTERSÉGES INTELLIGENCIA (fő LLM) ezt hívja meg
        [KernelFunction("GenerateTopicRecommendations")]
        [Description("3 témajavaslatot generál a felhasználónak egy többszereplős AI Ágens (Agents API) folyamat segítségével. Csak akkor hívd meg, ha a felhasználó KIFEJEZETTEN KÉRI, hogy ajánlj neki témákat, miről beszélgessetek.")]
        public async Task<string> GenerateRecommendationsAsync(Kernel kernel)
        {
            // ==========================================
            // MICROSOFT.SEMANTICKERNEL.AGENTS API MEGOLDÁS
            // ==========================================

            // 1. Adatgyűjtő Ágens (Felruházva egy adatbázis hozzáféréssel a DataGathererPlugin-on keresztül)
            var dataKernel = kernel.Clone();
            dataKernel.Plugins.AddFromObject(new DataGathererPlugin(_dbService, _userId), "DataTools");
            var dataAgent = new ChatCompletionAgent
            {
                Name = "DataGatherer",
                Instructions = "A te feladatod, hogy hívd meg a 'GetRecentUserMessages' eszközt (toolt) és nyerd ki a felhasználó korábbi üzeneteit. Semmi más dolgod nincs, utána csak add át a nyers kiolvasott szöveget a társalgásba.",
                Kernel = dataKernel,
                Arguments = new KernelArguments(new Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings { ToolCallBehavior = Microsoft.SemanticKernel.Connectors.OpenAI.ToolCallBehavior.AutoInvokeKernelFunctions })
            };

            // 2. Profilozó Ágens
            var profilerAgent = new ChatCompletionAgent
            {
                Name = "Profiler",
                Instructions = "Te egy profilozó AI vagy. Az eléd tárt adatgyűjtés nyers adataiból írj egy nagyon pontos profil összegzést a felhasználó viselkedéséről és aktuális érdeklődési köréről.",
                Kernel = kernel.Clone()
            };

            // 3. Téma Kitaláló (Stratéga) Ágens
            var strategistAgent = new ChatCompletionAgent
            {
                Name = "Strategist",
                Instructions = @"Te egy szigorú adatformázó stratéga vagy. A kapott profil alapján találj ki pontosan 3 izgalmas témát beszélgetésre.
SZIGORÚ SZABÁLYOK:
- Pontosan 3 témát írj!
- Mindegyik téma pontosan 1 mondat hosszú legyen!
- KIZÁRÓLAG a 3 mondatot írd le, minden mondatot egy új sorba!
- NE HASZNÁLJ sorszámozást vagy kötőjeleket!
- TILOS bármilyen bevezető vagy lezáró szöveg!",
                Kernel = kernel.Clone()
            };

            // Szekvenciális Agent futtatás közös ChatHistory-ban (lánc)
            ChatHistory workflowMemory = new ChatHistory();
            
            workflowMemory.AddUserMessage("Kérlek, indítsátok el az adatgyűjtést a beszélgetésekből!");
            await foreach (var message in dataAgent.InvokeAsync(workflowMemory))
            {
                workflowMemory.Add(message);
            }

            workflowMemory.AddUserMessage("Köszönöm az adatokat. Profilozó, kérlek a nyers adatokból készítsd el a profilt.");
            await foreach (var message in profilerAgent.InvokeAsync(workflowMemory))
            {
                workflowMemory.Add(message);
            }

            workflowMemory.AddUserMessage("Kész a profil! Stratéga ágens, alkoss 3 izgalmas javaslatot az elkészült felhasználói profilból maradéktalanul betartva a szabályokat.");
            await foreach (var message in strategistAgent.InvokeAsync(workflowMemory))
            {
                workflowMemory.Add(message);
            }

            // Az utolsó üzenet tartalmazza a stratégia (Strategist Agent) fix megformázott válaszát
            return workflowMemory.Last().Content ?? "Nem sikerült témát generálni a rendszerben.";
        }
    }
}