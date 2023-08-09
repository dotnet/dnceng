# .NET Engineering Services Azure AI Studio Chatbot Proof of Concept

## Organization

This PoC has two main components:

The backend is a Flask web framework (Python) that provides API access for the involved Azure Resources

The frontend is in React served by the backend and providing all user-facing functionality

## Azure Resources
- Service Principal: Used to grant the backend access to resources
- App Service: Hosts the website
- App Service Plan: Hosting the website 
- Azure OpenAI: Provides large language model for chatbot reasoning
- Cognitive Search Service: Queries and returns info to OpenAI for decision making


