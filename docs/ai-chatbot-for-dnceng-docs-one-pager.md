# AI Chatbot for DNCEng Docs

## Project Summary
The project aims to create an Azure OpenAI-powered service that can answer questions for .NET customers. Customers would enter their questions in a AI Chatbot, and the Chatbot would search through DNCEng documentation resources to provide an answer.

## Goals 
1. Use documentation from the "dotnet/arcade" repository
2. Establish an automated procedure for customers to get their documentation-involved questions answered in real-time
3. Admin and manipulate the documentation ingested by the AI Chatbot service
4. Incorporate DNCEng's "Sentiment Tracker" to help gather telemetry

## Stretch Goal(s)
1. Use documentation from the "dotnet/dnceng" repository
2. Use documentation from the public Wiki
3. Use documentation from the internal Wiki

## Current Investigation(s)
- PoC support for markdown file types

## Unknowns
1. How to pull documentation from the multiple existing resources
2. Automate the service to ingest new and updated documentation
3. Store the questions and Chatbot responses for telemetry

## Measurable Metric of Success
1. Comparing the Chatbot's responses to non-bar FR questions

## Potential Risks
1. Giving responses that expose internal information
2. Discontinuation of the Azure OpenAI resource

## Expected Telemetry
1. Collection of questions the AI Chatbot service was not able to answer
2. "Is this helpful, Like or Dislike" feature along with customer provided feedback

## Stakeholders
1. .NET Engineering Services Team
2. .NET Engineering Customers

## Existing Examples
- https://github.com/Azure-Samples/azure-search-openai-demo/
- https://github.com/Azure-Samples/azure-search-openai-demo-csharp/

## How would the technology interact with the customer?
- The Azure OpenAI's prompt-driven conversation would be used in the project. The user would type and send a message to ask questions. The AI would analyze the user's question and send a response message to the user
- The customer would have the ability to rate the accuracy or usefulness of the AI Chatbot through a "Like or Dislike feature" along with the decision to provide feedback