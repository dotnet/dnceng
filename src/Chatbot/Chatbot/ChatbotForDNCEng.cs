// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.22.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using Microsoft.Bot.Builder.Integration;

using AdaptiveCards;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using static System.Environment;
using static System.Net.Mime.MediaTypeNames;
using System.Text.Json.Nodes;
using System.Reflection;
using Azure.Identity;

namespace Chatbot
{
    public class ChatbotForDNCEng : ActivityHandler
    {
        private readonly string[] _cards =
        {
            Path.Combine(".", "Resources", "FeedbackCard.json"),
            Path.Combine(".", "Resources", "ContactSheet.json"),
        };

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            // Send a welcome message to the user and tell them what actions they may perform to use this bot
            await SendWelcomeMessageAsync(turnContext, cancellationToken);
        }

        private static async Task SendWelcomeMessageAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            const String Greeting = "Welcome! My name is DaniBob. How may I help you today?";
            const String HowToQuitInstructions = "Enter q, Q, quit, exit, or bye to end this chat.";
            const String HelpString = "Ask me a question or select one of the suggested options.";

            var OnMemberAddedMessage = Greeting + System.Environment.NewLine + HowToQuitInstructions;
            foreach (var member in turnContext.Activity.MembersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(
                        MessageFactory.Text(OnMemberAddedMessage),
                        cancellationToken: cancellationToken);
                    await SendSuggestedActionsAsync(HelpString, turnContext, cancellationToken);
                }
            }
        }

        // Predefined Options
        // From bot framework samples
        private static async Task SendSuggestedActionsAsync(string parentMessage, ITurnContext turnContext, CancellationToken cancellationToken)
        {
            var reply = MessageFactory.Text(parentMessage);

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
        }

        // This method allows the bot to respond to a user message
        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            // Error handling
            if (turnContext == null)
            {
                throw new ArgumentNullException(nameof(turnContext));
            }

            // Get user input
            var option = turnContext.Activity.Text.ToLower();
            var userRequest = turnContext.Activity.Text;

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
                    var cardAttachment = CreateAdaptiveCardAttachment(_cards[0]);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(cardAttachment), cancellationToken);
                    break;
                case "contact":
                    var contactAttachment = CreateAdaptiveCardAttachment(_cards[1]);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(contactAttachment), cancellationToken);
                    await SendSuggestedActionsAsync(followup, turnContext, cancellationToken);
                    break;
                case "permissions":
                case "pat":
                    await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                    await SendSuggestedActionsAsync(followup, turnContext, cancellationToken);
                    break;
                default:
                    var aiResponse = AskOpenAI(userRequest);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(aiResponse), cancellationToken);
                    await SendSuggestedActionsAsync(followup, turnContext, cancellationToken);
                    break;
            }
        }

        // From bot framework samples
        private static Attachment CreateAdaptiveCardAttachment(string filePath)
        {
            var adaptiveCardJson = File.ReadAllText(filePath);
            var adaptiveCardAttachment = new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCardJson),
            };
            return adaptiveCardAttachment;
        }

        private static Attachment FormatLinks(String response, List<(String, String)>links)
        {
            AdaptiveCard card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 0));

            card.Body.Add(new AdaptiveTextBlock()
            {
                Text = response,
                Size = AdaptiveTextSize.Default,
                Wrap = true,
            });

            foreach (var link in links) 
            {
                var action = new AdaptiveOpenUrlAction()
                {
                    Title = link.Item1,
                    Url = new Uri(link.Item2),
                };
                card.Actions.Add(action);
            }

            string adaptiveCardJson = card.ToJson();

            var adaptiveCardAttachment = new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCardJson),
            };

            return adaptiveCardAttachment;
        }

        private static Attachment AskOpenAI(String question)
        {
            /* 
             * The line below disables the warning because the.AddDataSource
             * is an experimental feature a part of the newest release.
             *
             * Source: https://learn.microsoft.com/en-us/azure/ai-services/openai/use-your-data-quickstart?tabs=command-line%2Cpython-new&pivots=programming-language-csharp
             */

#pragma warning disable AOAI001

            var servicePrompt = "You are an AI assistant for Microsoft’s Dotnet Core Engineering team" +
                                "that helps the team and other Microsoft employees find information using " +
                                "Dotnet’s repositories on GitHub. You like to give examples whenever possible." +
                                "You cite your references with the filepath in the format of [filepath].";

            var chatClient = CreateChatClient();
            var chatCompletionsOptions = ConfigChatOptions();

            // Format the chat completion and send the request 
            var messages = new List<ChatMessage>
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
            var messageContext = completion.GetAzureMessageContext();
            var citations = messageContext.Citations;

            // Format each citation to send to the user
            /*
             * TODO for now keep citation url, but eventually when we get full paths from github we can get the github link
             * from a search on the file path and use that link instead
             * 
             * Citations - repetitive bc referencing different chunks of the same file
             */
            var currentCitation = 1;
            var citationInfo = new List<(String, String)>();
            foreach (var citation in citations)
            {
                var citationTitle = "Doc " + currentCitation.ToString() + ": " + citation.Filepath;
                citationInfo.Add((citationTitle, citation.Url));
                currentCitation++;
            }

            var responseCard = FormatLinks(completion.Content[0].Text, citationInfo);
            return responseCard;

        }

        private static ChatClient CreateChatClient()
        {
            // For chat client
            var openAIEndpoint = GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var openAIKey = GetSecrets("AzureOpenAiApiKey");
            var openAIDeploymentName = GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_ID");

            AzureOpenAIClient azureClient = new(new Uri(openAIEndpoint), new ApiKeyCredential(openAIKey.Result));

            // Creates OpenAI Chat completions client
            var chatClient = azureClient.GetChatClient(openAIDeploymentName);

            return chatClient;
        }

        private static ChatCompletionOptions ConfigChatOptions()
        {
            // Search service variables
            var searchEndpoint = GetEnvironmentVariable("AZURE_AI_SEARCH_ENDPOINT");
            var searchKey = GetSecrets("AiSearchApiKey");
            var searchIndex = GetEnvironmentVariable("AZURE_AI_SEARCH_INDEX");

            // Configure the chat completions options to use our data
            var chatCompletionsOptions = new ChatCompletionOptions();
            chatCompletionsOptions.AddDataSource(new AzureSearchChatDataSource()
            {
                Endpoint = new Uri(searchEndpoint),
                IndexName = searchIndex,
                Authentication = DataSourceAuthentication.FromApiKey(searchKey.Result),
            });

            return chatCompletionsOptions;

            // TODO maybe change the n because model might by default be generating more than 1 answer
            // and it might be collecting more citations because of the other answers

            // Maybe it could be the retrieved docs that changes the number of citations?
            // Use regex matching to see which docs we need to link as a citation?
        }

        private static async Task<String> GetSecrets(String secretName)
        {
            String keyVaultName = "DncengChatbotKV";
            var kvUri = "https://" + keyVaultName + ".vault.azure.net";
            var client = new SecretClient(new Uri(kvUri), new DefaultAzureCredential());

            var secret = await client.GetSecretAsync(secretName);
            return secret.Value.Value;
        }

    }
}
