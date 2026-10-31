# diagram

External Tool: **StateMachine Diagrammer** (`StatePipes.Diagrammer.exe`).

Renders one PDF per state machine of the service, from the built `.Service` assembly, into
`%ProgramData%\StatePipes\StatePipes.Diagrammer\`.

## Run the tool

Build first (`dotnet build <SolutionDir>\<Solution>.slnx`), then:

```
& "<Diagrammer>" -headless -r "<SolutionDir>\" -s <Solution>.slnx -p <Prefix>.csproj -c "<SolutionDir>\<Prefix>\obj\Debug\net10.0\" --out "<SolutionDir>\<Prefix>\Diagrams"
```

It prints `Diagram written: <path>` for each PDF. Read each PDF and compare it with the approved model from
`statepipes-domain-modeling`: every state, transition, guard and published event in the model should appear. The PDFs
are generated output; don't commit them unless the user asks.

## Fallback (tool without headless support)

Ask the user to run **Tools → StateMachine Diagrammer** with `<Prefix>` active (the PDFs then go to
`%ProgramData%\StatePipes\StatePipes.Diagrammer\`), and to compare them with the approved model.

No files in the solution change.
