# AI Chatbot for DNCEng Docs

## Project Summary
The project aims to create an Azure AI-powered service that can answer questions from .NET customers. Customers can enter their questions in our prompt software service, and the service will search through our documentation resources to provide an answer.

## Goals 
1. Minimize the necessity of answering customer issues that can be resolved by referring to the "dotnet/arcade" GitHub repository.
2. Establish an automated and efficient way for customers to have their documentation-involved questions answered conveniently.

## Stretch Goal(s)
1. Using documentation from the dotnet/dnceng repository

## Unknowns
1. How to pull documentation from the multiple existing resources
2. Automate the service to ingest newly created documentation

## Stakeholders
1. .NET Engineering Services Team
2. .NET Engineering Customers

## Existing Examples
- https://github.com/Azure-Samples/azure-search-openai-demo/
- https://github.com/Azure-Samples/azure-search-openai-demo-csharp/

## How would the technology interact with the customer?
- The Azure OpenAI's prompt-driven conversation would be used in the project. The user would type and send a message to ask questions. The AI would analyze the user's question and send a response message to the user.

## From where are the resources and documents sourced?
- The resources and documents are sourced from the "Documentation" directory located in the public GitHub repository "dotnet/arcade".

## Which particular question scenarios will be tackled?
- We are addressing questions that can be found in the "Documentation" directory of the public GitHub repository "dotnet/arcade". For instance, the question, "What are SDL Scripts", can be accessed directly in the "HowToAddSDLRunToPipeline.md" file.