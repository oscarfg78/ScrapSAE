## ADDED Requirements

### Requirement: Centralized README documentation
The project SHALL include a `README.md` file at the root.

#### Scenario: Developer onboarding
- **WHEN** a new developer clones the repository
- **THEN** they can read the README to understand the project purpose and modules

### Requirement: Document execution instructions
The README SHALL provide clear instructions on how to run the `Api` and `Worker` projects locally.

#### Scenario: Running the Api
- **WHEN** a user wants to run the Api
- **THEN** they can find the `dotnet run` command and `appsettings.json` requirements in the README

### Requirement: Track module statuses
The README SHALL list the status of all modules (Core, Infrastructure, Worker, Api, Desktop, Web/Extension).

#### Scenario: Checking module status
- **WHEN** a user checks the README
- **THEN** they see which modules are stable, which are in development, and which are deprecated
