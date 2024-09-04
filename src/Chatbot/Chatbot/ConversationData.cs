using OpenAI.Chat;
using System.Collections.Generic;

namespace Chatbot
{
    // Defines a state property used to track conversation data.
    public class ConversationData
    {
        // The time-stamp of the most recent incoming message.
        public List<(string, string)> ConversationHistory { get; set; } = new List<(string, string)>();

    }
}