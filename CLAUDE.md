# Lucid IT Agent - Claude Code Instructions

## Project Overview
Enterprise AI-powered helpdesk automation system. Processes ServiceNow tickets and executes IT operations against Active Directory.

## Repository Structure
- `agent/` - Python agent (Griptape framework)
- `admin/dotnet/` - Blazor Server Admin Portal (.NET 8)
- `tool-server/dotnet/` - .NET Tool Server (AD operations)
- `tool-server/python/` - Python Tool Server (legacy)
- `docs/` - Architecture docs, ADRs, prompts
- `env-setup/` - Environment setup scripts

## Git Workflow
- **Always commit after completing work** with a descriptive commit message
- Use conventional commit format: `feat:`, `fix:`, `refactor:`, `docs:`, `chore:`
- When working on a feature branch, commit to that branch
- Do NOT push without explicit permission

## Technology Stack
- **Admin Portal**: Blazor Server, .NET 8, MudBlazor, Entity Framework Core, SQLite (dev) / SQL Server (prod)
- **Agent**: Python 3.11+, Griptape 1.9.0, Pydantic
- **Tool Server**: .NET 8 Minimal API, System.DirectoryServices

## Coding Standards
- C# follows standard .NET conventions
- Python follows PEP 8
- Use record types for DTOs in C#
- Use Pydantic models for Python data classes

## Key Patterns
- ServiceAccount as unified provider pattern (LLM, ServiceNow, ToolServer credentials)
- Capability routing for tool server discovery
- Three-layer architecture: Core (entities/interfaces) → Infrastructure (EF/repos) → Web (Blazor/API)

## Testing
- Run `dotnet build` from `admin/dotnet/` to verify Admin Portal
- Run `dotnet build` from `tool-server/dotnet/` to verify Tool Server
- Star Wars themed test users (Luke Skywalker, Han Solo, etc.) in montanifarms.com domain

## Important Notes
- Griptape 1.9.0 uses `from griptape.drivers.prompt.ollama import OllamaPromptDriver` (nested path)
- ServiceNow PDI: dev341394.service-now.com, assignment group "Help Desk"
- Domain: montanifarms.com
