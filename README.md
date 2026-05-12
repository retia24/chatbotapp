# FunChatBotApp

A powerful, modular chatbot application built with ASP.NET Core, .NET 9, Semantic Kernel, and Azure integrations. FunChatBotApp offers real-time conversation powered by OpenAI, robust authentication, and seamless cloud service connectivity.

## Features

- **Conversational AI**: Integrates OpenAI and Semantic Kernel to deliver advanced chat experiences.
- **Identity & Security**: Leverages ASP.NET Core Identity for authentication and robust user management.
- **Azure Native**: Uses Azure Key Vault for secure secrets, Cosmos DB for scalable storage, and more.
- **Rate Limiting**: Built-in rate limiting to protect endpoints and prevent abuse.
- **Extensible Architecture**: Plugin & service-oriented structure for easy customization.
- **Test Coverage**: Unit and integration tests powered by xUnit and Moq.
- **CI/CD Ready**: Designed for cloud-native deployments and GitHub Actions workflows.

## Tech Stack

- **.NET 9.0** (ASP.NET Core)
- **Azure OpenAI, Cosmos DB, Key Vault, Text Analytics**
- **Microsoft.SemanticKernel**
- **Entity Framework Core (SQL Server)**
- **Authentication:** ASP.NET Core Identity
- **Testing:** xUnit, Moq

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- Azure account (for OpenAI/Cosmos/Key Vault services)
- (Recommended) Visual Studio 2022+ or VS Code

### Setup

1. **Clone the Repository**
    ```sh
    git clone https://github.com/retia24/chatbotapp.git
    cd chatbotapp
    ```

2. **Configure Secrets**
   - Edit `FunChatBotApp/appsettings.json` with your Azure and OpenAI credentials, or configure via user secrets/local environment variables.

3. **Restore Dependencies**
    ```sh
    dotnet restore
    ```

4. **Run Database Migrations**
    ```sh
    dotnet ef database update --project FunChatBotApp
    ```

5. **Run the Application**
    ```sh
    dotnet run --project FunChatBotApp
    ```

   - Access at `https://localhost:5001`

### Running Tests

```sh
dotnet test
```

## Configuration

- **Azure Key Vault, OpenAI, Cosmos DB, and other credentials** are set in `appsettings.json` or loaded from environment/configuration providers.
- Sensitive keys & credentials should not be committed to source control.

## Project Structure

```
FunChatBotApp/
│
├─ Components/          # UI components
├─ Data/                # Entity Framework data models and context
├─ Models/              # Domain models
├─ Plugins/             # Modular plugin code
├─ Services/            # Chat/persistence/utility services
├─ wwwroot/             # Static files
├─ Program.cs           # Application entry point
└─ appsettings.json     # Main configuration file

FunChatBotApp.Tests/     # Test projects and test utilities
```

## Contribution

Contributions are welcome! Please:

1. Open an issue or feature request first
2. Fork the repo and make your changes
3. Add or update unit tests as necessary
4. Open a pull request describing your changes

## License

[MIT](LICENSE)

## Acknowledgements

- [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel)
- [Azure for .NET Developers](https://docs.microsoft.com/en-us/dotnet/azure/)
- [OpenAI](https://platform.openai.com/docs/)

---

**For questions or support, please use the GitHub issues section.**
