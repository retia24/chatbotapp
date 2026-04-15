using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FunChatBotApp.Plugins
{
    public class TopicExtractionFilter : IFunctionInvocationFilter
    {
        public List<string>? ExtractedTopics { get; private set; }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            await next(context);

            // Csak a Téma Generáló ágens kimenetét figyeljük
            if (context.Function.Name == "GenerateTopicRecommendations")
            {
                var resultText = context.Result?.ToString();
                if (!string.IsNullOrWhiteSpace(resultText))
                {
                    ExtractedTopics = resultText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                                .Select(t => t.Trim('-').Trim())
                                                .ToList();
                }
            }
        }
    }
}