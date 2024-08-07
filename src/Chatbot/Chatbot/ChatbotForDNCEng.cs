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
using Microsoft.ApplicationInsights;
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

        private readonly TelemetryClient _telemetryClient;
        private readonly IConfiguration _configuration;


        public ChatbotForDNCEng(TelemetryClient telemetryClient, IConfiguration configuration)
        {
            _configuration = configuration;
            _telemetryClient = telemetryClient;
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            _telemetryClient.TrackTrace("Bot is handling member added.");
            // Send a welcome message to the user and tell them what actions they may perform to use this bot
            await SendWelcomeMessageAsync(turnContext, cancellationToken);
        }

        private async Task SendWelcomeMessageAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            _telemetryClient.TrackTrace("Bot is sending welcome message.");
            Attachment cardAttachment = CreateAdaptiveCardAttachment(_cards["WelcomeCard"]);
            
            foreach (var member in turnContext.Activity.MembersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(cardAttachment), cancellationToken);
                    await SendSuggestedActionsAsync("", turnContext, cancellationToken);
                }
            }
        }

        // Predefined Options
        // From bot framework samples
        private async Task SendSuggestedActionsAsync(string parentMessage, ITurnContext turnContext, CancellationToken cancellationToken)
        {
            Activity reply = MessageFactory.Text(parentMessage);

            reply.SuggestedActions = new SuggestedActions()
            {
                Actions = new List<CardAction>()
                {
                    new CardAction() { Title = "Request permissions and approval", Type = ActionTypes.ImBack, Value = "Permissions" },
                    new CardAction() { Title = "Refresh personal access token", Type = ActionTypes.ImBack, Value = "PAT" } ,
                    new CardAction() { Title = "Who do I contact for...", Type = ActionTypes.ImBack, Value = "Contact" },

                },
            };
            await turnContext.SendActivityAsync(reply, cancellationToken);
            _telemetryClient.TrackTrace("Bot has sent Suggestion Actions.");
        }

        // This method allows the bot to respond to a user message
        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            _telemetryClient.TrackTrace("Bot is handling message activity.");
            // Error handling
            ArgumentNullException.ThrowIfNull(turnContext);

            // Get user input
            String option = turnContext.Activity.Text.ToLower();
            String userRequest = turnContext.Activity.Text;

            // Constants -> Predefined responses
            const String response = "Please contact the First Responder's channel.";
            const String followup = "Is there anything else I can help you with today?";

            switch (option)
            {
                case "exit":
                case "q":
                case "quit":
                case "bye":
                case "no":
                case "nope":
                case "n/a":
                    Attachment cardAttachment = CreateAdaptiveCardAttachment(_cards["FeedbackCard"]);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(cardAttachment), cancellationToken);
                    break;
                case "contact":
                    Attachment contactAttachment = CreateAdaptiveCardAttachment(_cards["ContactSheet"]);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(contactAttachment), cancellationToken);
                    await SendSuggestedActionsAsync(followup, turnContext, cancellationToken);
                    break;
                case "permissions":
                case "pat":
                    await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                    await SendSuggestedActionsAsync(followup, turnContext, cancellationToken);
                    break;
                default:
                    Attachment aiResponse = await AskOpenAI(userRequest);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(aiResponse), cancellationToken);
                    await SendSuggestedActionsAsync(followup, turnContext, cancellationToken);
                    break;
            }
        }

        // From bot framework samples
        private static Attachment CreateAdaptiveCardAttachment(string filePath)
        {
            String adaptiveCardJson = File.ReadAllText(filePath);
            Attachment adaptiveCardAttachment = new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCardJson),
            };
            return adaptiveCardAttachment;
        }

        private static Attachment FormatLinks(String response, List<(String, String)> links)
        {
            // This method formats the Azure OpenAI answer as an attachment to send back to the user
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

        private async Task<Attachment> AskOpenAI(String question)
        {
            _telemetryClient.TrackTrace("Bot is making a request to Azure OpenAI.");
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

            // Format the chat completion and send the request 
            List<ChatMessage> messages = new List<ChatMessage>
            { 
                // If there is old chat history that you want to include, you would do it here
                // Adds the service prompt, gives context to the bot on how it should respond
                new SystemChatMessage(servicePrompt),
                // Adds the user's question
                new UserChatMessage(question),
            };

            // Gets the AI generated response
            ChatCompletion completion = chatClient.CompleteChat(messages, chatCompletionsOptions);

            // Gets the message context: contains the citations, convo intent, and info about retrieved docs
            AzureChatMessageContext messageContext = completion.GetAzureMessageContext();
            IReadOnlyList<AzureChatCitation> citations = messageContext.Citations;

            // Format each citation to send to the user
            /*
             * TODO for now keep citation url, but eventually when we get full paths from github we can get the github link
             * from a search on the file path and use that link instead
             * 
             * Citations - repetitive bc referencing different chunks of the same file
             */
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

        private async Task<ChatClient> CreateChatClient()
        {
            // This method creates the chat client so the bot can get answers from Azure OpenAI
            // For chat client
            _telemetryClient.TrackTrace("Bot is creating chat client.");
            String openAIEndpoint = "https://testing-bot.openai.azure.com/";
            String openAIKey = await GetSecrets("AzureOpenAiApiKey");
            String openAIDeploymentName = "explorers-test";

            AzureOpenAIClient azureClient = new(new Uri(openAIEndpoint), new ApiKeyCredential(openAIKey));

            // Creates OpenAI Chat completions client
            ChatClient chatClient = azureClient.GetChatClient(openAIDeploymentName);
            _telemetryClient.TrackTrace("Bot successfully created chat client.");

            return chatClient;
        }

        private async Task<ChatCompletionOptions> ConfigChatOptions()
        {
            // This method creates the search client so that the bot can use our data instead of ChatGPT's training data
            // Search service variables
            _telemetryClient.TrackTrace("Bot is configuring search options.");
            String searchEndpoint = "https://testingbot-search.search.windows.net";
            String searchKey = await GetSecrets("AiSearchApiKey");
            String searchIndex = "all-data-auto-uploaded";

            // Configure the chat completions options to use our data
            ChatCompletionOptions chatCompletionsOptions = new ChatCompletionOptions();
            chatCompletionsOptions.AddDataSource(new AzureSearchChatDataSource()
            {
                Endpoint = new Uri(searchEndpoint),
                IndexName = searchIndex,
                Authentication = DataSourceAuthentication.FromApiKey(searchKey),
            });
            _telemetryClient.TrackTrace("Bot successfully created search options.");

            return chatCompletionsOptions;

            // TODO maybe change the n because model might by default be generating more than 1 answer
            // and it might be collecting more citations because of the other answers

            // Maybe it could be the retrieved docs that changes the number of citations?
            // Use regex matching to see which docs we need to link as a citation?
        }

        private async Task<String> GetSecrets(String secretName)
        {
            // This method gets secrets needed to make the chat client and the search client
            _telemetryClient.TrackTrace("Bot is getting secrets.");
            String keyVaultName = _configuration["KeyVaultName"];
            String kvUri = "https://" + keyVaultName + ".vault.azure.net";

            DefaultAzureCredential credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions()
            {
                ManagedIdentityClientId = _configuration["MicrosoftAppId"]
            }
            ); 
            SecretClient client = new SecretClient(new Uri(kvUri), credential);
            _telemetryClient.TrackTrace("Bot created secret client.");

            KeyVaultSecret secret = await client.GetSecretAsync(secretName);
            _telemetryClient.TrackTrace("Bot is retrieved secret successfully.");

            return secret.Value;
        }

        // Notes
        /*
         * TODO: new method: create messages in order to get previous context 
         * Retrieve previous user messages up to 10: 5 bot/assistnat responses and 5 user messages
         * Then add the service prompt and all previous messages
         * Add the current message
         * Print what messages looks like
         * 
         */

        /*
         * Need to save Conversation state in order to save previous messages
         * When do I delete the conversation and how do I restrict it to only a certain number of convos
         * How do I know the convo is over?
         */
    }
}
