# diagram

External Tool: **StateMachine Diagrammer** (`StatePipes.Diagrammer.exe`).

Renders one PDF per state machine of the service, from the built `.Service` assembly, into
`%ProgramData%\StatePipes\StatePipes.Diagrammer\`.

## Who does it

The diagrammer builds through a running Visual Studio, so ask the user to run **Tools → StateMachine
Diagrammer** with `<Prefix>` active, and to compare the PDF with the approved model from
`statepipes-domain-modeling`: every state, transition, guard and published event in the model should appear.

No files in the solution change.
