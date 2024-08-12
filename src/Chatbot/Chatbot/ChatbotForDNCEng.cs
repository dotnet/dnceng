// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.22.0
using Newtonsoft.Json;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AdaptiveCards;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using Microsoft.Extensions.Logging;

namespace Chatbot
{
    public class ChatbotForDNCEng : ActivityHandler
    {
        private readonly Dictionary<string, string> _cards = new Dictionary<string, string>()
        {
            {"FeedbackCard", Path.Combine(".", "Resources", "FeedbackCard.json")},
            {"ContactSheet", Path.Combine(".", "Resources", "ContactSheet.json")},
            {"WelcomeCard", Path.Combine(".", "Resources", "WelcomeCard.json")}
        };

        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;


        public ChatbotForDNCEng(ILogger<ChatbotForDNCEng> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Bot is handling member added.");
            await SendWelcomeMessageAsync(turnContext, cancellationToken);
        }

        public async Task SendWelcomeMessageAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Bot is sending welcome message.");
            Attachment cardAttachment = CreateAdaptiveCardAttachment(_cards["WelcomeCard"]);
            
            foreach (var member in turnContext.Activity.MembersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    IMessageActivity welcomeMessage = MessageFactory.Attachment(cardAttachment);
                    welcomeMessage.SuggestedActions = CreateSuggestedActions();
                    await turnContext.SendActivityAsync(welcomeMessage, cancellationToken);
                }
            }
        }

        // From bot framework samples
        public static SuggestedActions CreateSuggestedActions()
        {
            SuggestedActions actions = new SuggestedActions()
            {
                Actions = new List<CardAction>()
                {
                    new CardAction() { Title = "Give feedback", Type = ActionTypes.ImBack, Value = "Feedback" } ,
                    new CardAction() { Title = "Who do I contact for...", Type = ActionTypes.ImBack, Value = "Contact" },

                },
            };
            return actions;
        }

        // This method allows the bot to respond to a user message
        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Bot is handling message activity.");
            // Error handling
            ArgumentNullException.ThrowIfNull(turnContext);

            // Get user input
            String option = turnContext.Activity.Text.ToLower();
            String userRequest = turnContext.Activity.Text;

            const String followup = "Is there anything else I can help you with today?";

            switch (option)
            {
                case "feedback":
                    Attachment cardAttachment = CreateAdaptiveCardAttachment(_cards["FeedbackCard"]);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(cardAttachment), cancellationToken);
                    break;
                case "contact":
                    Attachment contactAttachment = CreateAdaptiveCardAttachment(_cards["ContactSheet"]);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(contactAttachment), cancellationToken);
                    break;
                default:
                    Attachment aiResponse = await AskOpenAI(userRequest);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(aiResponse), cancellationToken);
                    break;
            }
            IMessageActivity followupActions = MessageFactory.Text(followup);
            followupActions.SuggestedActions = CreateSuggestedActions();
            await turnContext.SendActivityAsync(followupActions, cancellationToken);
        }

        // From bot framework samples
        public static Attachment CreateAdaptiveCardAttachment(string filePath)
        {
            String adaptiveCardJson = File.ReadAllText(filePath);
            Attachment adaptiveCardAttachment = new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCardJson),
            };
            return adaptiveCardAttachment;
        }

        public static Attachment FormatLinks(String response, List<(String, String)> links)
        {
            // This method formats the Azure OpenAI answer and citations as an attachment to send back to the user
            AdaptiveCard card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 0));

            card.Body.Add(new AdaptiveTextBlock()
            {
                Text = response,
                Size = AdaptiveTextSize.Default,
                Wrap = true,
            });

            foreach (var link in links) 
            {
                AdaptiveOpenUrlAction action = new AdaptiveOpenUrlAction()
                {
                    Title = link.Item1,
                    Url = new Uri(link.Item2),
                };
                card.Actions.Add(action);
            }

            string adaptiveCardJson = card.ToJson();

            Attachment adaptiveCardAttachment = new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCardJson),
            };

            return adaptiveCardAttachment;
        }

        public async Task<Attachment> AskOpenAI(String question)
        {
            _logger.LogDebug("Bot is making a request to Azure OpenAI.");
            /* 
             * The line below disables the warning because the.AddDataSource
             * is an experimental feature a part of the newest release.
             *
             * Source: https://learn.microsoft.com/en-us/azure/ai-services/openai/use-your-data-quickstart?tabs=command-line%2Cpython-new&pivots=programming-language-csharp
             */

#pragma warning disable AOAI001

            String servicePrompt = "You are an AI assistant for Microsoft’s .NET Engineering team" +
                                "that helps the team and other Microsoft employees find information using " +
                                ".NET’s repositories on GitHub. You like to give examples whenever possible." +
                                "You cite your references with the filepath in the format of [filepath].";

            ChatClient chatClient = await CreateChatClient();
            ChatCompletionOptions chatCompletionsOptions = await ConfigChatOptions();

            List<ChatMessage> messages = new List<ChatMessage>
            { 
                // If there is old chat history that you want to include, you would do it here
                // Adds the service prompt, gives context to the bot on how it should respond
                new SystemChatMessage(servicePrompt),
                // Adds the user's question
                new UserChatMessage(question),
            };

            ChatCompletion completion = await chatClient.CompleteChatAsync(messages, chatCompletionsOptions);

            // Gets the message context: contains the citations, convo intent, and info about retrieved docs
            AzureChatMessageContext messageContext = completion.GetAzureMessageContext();
            IReadOnlyList<AzureChatCitation> citations = messageContext.Citations;

            int currentCitation = 1;
            List<(String, String)> citationInfo = new List<(String, String)>();
            foreach (var citation in citations)
            {
                String citationTitle = "Doc " + currentCitation.ToString() + ": " + citation.Filepath;
                citationInfo.Add((citationTitle, citation.Url));
                currentCitation++;
            }

            Attachment responseCard = FormatLinks(completion.Content[0].Text, citationInfo);
            return responseCard;

        }

        public async Task<ChatClient> CreateChatClient()
        {
            _logger.LogDebug("Bot is creating chat client.");
            String openAIEndpoint = "https://testing-bot.openai.azure.com/";
            String openAIKey = await GetSecrets("AzureOpenAiApiKey");
            String openAIDeploymentName = "explorers-test";

            AzureOpenAIClient azureClient = new(new Uri(openAIEndpoint), new ApiKeyCredential(openAIKey));

            ChatClient chatClient = azureClient.GetChatClient(openAIDeploymentName);
            _logger.LogDebug("Bot successfully created chat client.");

            return chatClient;
        }

        public async Task<ChatCompletionOptions> ConfigChatOptions()
        {
            _logger.LogDebug("Bot is configuring search options.");
            String searchEndpoint = "https://testingbot-search.search.windows.net";
            String searchKey = await GetSecrets("AiSearchApiKey");
            String searchIndex = "all-data-auto-uploaded-daily";

            // Configure the chat completions options to use our data instead of ChatGPT's training data
            ChatCompletionOptions chatCompletionsOptions = new ChatCompletionOptions();
            chatCompletionsOptions.AddDataSource(new AzureSearchChatDataSource()
            {
                Endpoint = new Uri(searchEndpoint),
                IndexName = searchIndex,
                Authentication = DataSourceAuthentication.FromApiKey(searchKey),
            });
            _logger.LogDebug("Bot successfully created search options.");

            return chatCompletionsOptions;
        }

        private async Task<String> GetSecrets(String secretName)
        {
            _logger.LogDebug("Bot is getting secrets.");
            String keyVaultName = _configuration["KeyVaultName"];
            String kvUri = "https://" + keyVaultName + ".vault.azure.net";

            DefaultAzureCredential credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions()
            {
                ManagedIdentityClientId = _configuration["MicrosoftAppId"]
            }
            ); 
            SecretClient client = new SecretClient(new Uri(kvUri), credential);
            _logger.LogDebug("Bot created secret client.");

            KeyVaultSecret secret = await client.GetSecretAsync(secretName);
            _logger.LogDebug("Bot is retrieved secret successfully.");

            return secret.Value;
        }
    }
}
