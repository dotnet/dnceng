﻿// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.22.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;
using Microsoft.Bot.Builder.Integration;

// For working with Azure OpenAI
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using OpenAI;
using OpenAI.Chat;
using System.Text.Json.Nodes;
using static System.Environment;
using static System.Net.Mime.MediaTypeNames;

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
            var userRequest = turnContext.Activity.Text;

            // Constants -> Predefined responses
            const String response = "Please contact the First Responder's channel.";
            const String followup = "Is there anything else I can help you with today?";

            switch (userRequest)
            {
                case "exit":
                case "q":
                case "quit":
                case "bye":
                    var cardAttachment = CreateAdaptiveCardAttachment(_cards[0]);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(cardAttachment), cancellationToken);
                    break;
                case "Contact":
                    var contactAttachment = CreateAdaptiveCardAttachment(_cards[1]);
                    await turnContext.SendActivityAsync(MessageFactory.Attachment(contactAttachment), cancellationToken);
                    await SendSuggestedActionsAsync(followup, turnContext, cancellationToken);
                    break;
                case "Permissions":
                case "PAT":
                    await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                    await SendSuggestedActionsAsync(followup, turnContext, cancellationToken);
                    break;
                default:
                    var aiResponse = AskOpenAI(userRequest);
                    await turnContext.SendActivityAsync(MessageFactory.Text(aiResponse), cancellationToken);
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

        private static String AskOpenAI(String question)
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
                                "Dotnet’s repositories on GitHub. You like to give examples whenever possible.";

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
            foreach (var citation in citations)
            {
                Console.WriteLine(citation.Title);
                Console.WriteLine(citation.Content);
                Console.WriteLine(citation.Filepath);
                Console.WriteLine(citation.Url);
            }

            return completion.Content[0].Text;

        }

        private static ChatClient CreateChatClient()
        {
            // For chat client
            var openAIEndpoint = GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var openAIKey = GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            var openAIDeploymentName = GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_ID");

            AzureOpenAIClient azureClient = new(new Uri(openAIEndpoint), new ApiKeyCredential(openAIKey));

            // Creates OpenAI Chat completions client
            var chatClient = azureClient.GetChatClient(openAIDeploymentName);

            return chatClient;
        }

        private static ChatCompletionOptions ConfigChatOptions()
        {
            // Search service variables
            var searchEndpoint = GetEnvironmentVariable("AZURE_AI_SEARCH_ENDPOINT");
            var searchKey = GetEnvironmentVariable("AZURE_AI_SEARCH_API_KEY");
            var searchIndex = GetEnvironmentVariable("AZURE_AI_SEARCH_INDEX");

            // Configure the chat completions options to use our data
            var chatCompletionsOptions = new ChatCompletionOptions();
            chatCompletionsOptions.AddDataSource(new AzureSearchChatDataSource()
            {
                Endpoint = new Uri(searchEndpoint),
                IndexName = searchIndex,
                Authentication = DataSourceAuthentication.FromApiKey(searchKey),
            });

            return chatCompletionsOptions;

        }

    }
}
