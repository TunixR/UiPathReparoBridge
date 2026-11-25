# Glossary

**Reparo-wide terms live in home's
[`GLOSSARY.md`](https://github.com/TunixR/ReparoHome/blob/main/GLOSSARY.md)** (private;
`../ReparoHome/GLOSSARY.md` in a local clone): Robot, Bridge, Platform, Process, Event, Event id,
Activity, Variables, Process model, Logged event, UI log, Completion check, Run outcome, Modality,
Observation, Recovery action, the kinds of Failure, Recovery, Continuation and the rest. They bind
this repo. This file adds only what is particular to UiPath and this Bridge, and contradicts
nothing there.

A bare term always means home's. When this repo means UiPath's own concept under a word home also
uses, it writes "UiPath" in front: a UiPath activity is not an Activity. Code is exempt where
UiPath's own type names the thing, such as `System.Activities.Activity` or `CodeActivity`.

## Running

**UiPath Robot**: UiPath's executor, which runs UiPath processes on a machine. It is the Robot for
UiPath, whether or not the process it runs uses the Bridge. Orchestrator also calls a robot account
or licence a robot; write "robot account" for that sense.

**Job**: UiPath's word for one run of a process by a UiPath Robot.

**Healing Agent**: UiPath's fixer of a UI problem inside one Modern UiPath activity, on the same
screen. A problem it heals never stops the run, so it is never a Failure and never reaches Reparo.
Its work is healing; Reparo's is a Recovery.
_Avoid_: recovery, recover (for Healing Agent's work)

## Building a process

**UiPath activity**: UiPath's executable block in a workflow, identified within its workflow file
by its `IdRef` (such as `NClick_1`). It is not an Activity. Every UiPath activity in a Process sits
inside a Reparo Event.
_Avoid_: activity (alone)

**Process project**: A Studio project of UiPath's Process type, beside Library and Test Automation.
It is not a Process: the Process is what the Bridge models from it.
_Avoid_: process (alone, for the project)

**Orchestrator process**: A package version of a Process project, deployed to an Orchestrator
folder so UiPath Robots can run jobs of it.
_Avoid_: process (alone, for the deployment)

**Workflow file**: One `.xaml` file of a Process project, rooted in a Sequence, a Flowchart or a
State Machine. A project's files are joined by Invoke Workflow File. A Process lives in one, for now: an
invoked workflow file runs whole, inside one Reparo Event.
_Avoid_: workflow (alone)

**Coded workflow**: A C# workflow file in a Process project, with no XAML and no `IdRef`.

**UiPath variable**: A named value UiPath scopes to a container, a workflow file or the project.
The UiPath variables declared on Reparo Process are the Process's Variables. Any other UiPath variable is
not one of the Variables.

**UiPath argument**: A workflow file's In, Out or In/Out parameter. It is not one of the Variables
unless the Bridge sends it as one.

## The Bridge's blocks

The UiPath Bridge's own UiPath activities, which give a Process its structure. Each block's Event id or
node id is its workflow file's stem and `IdRef`, such as `Main-ReparoEvent_5`.

**Reparo Process**: The block that holds a Process and its Variables. It catches the UI failures
that nothing inside it resolved, and it continues the run after a Recovery.

**Reparo Activity**: The block that groups the Events of one Activity.

**Reparo Event**: The block whose body is one Event. Its body holds UiPath activities, and any
UiPath control flow inside it runs whole as part of that Event.

**Reparo If** and **Reparo For Each**: The only control flow between Events: an exclusive gateway, and
a loop over a list that is one of the Variables, whose item is also one of the Variables.

## Other UiPath words

**Execution logs**: The logs a UiPath Robot writes, including what the Log Message activity adds.
They are not the UI log.

**REFramework**: UiPath's Robotic Enterprise Framework, a State Machine template for processes
that work through Transactions one by one. It retries a Transaction that ends in a System
exception, and reinitialises its applications first.

**Transaction**: One item of work REFramework hands to its `Process.xaml`, from an Orchestrator
queue or another source of data. Processing it once is one run of the Process. A retry processes
it again, so it is a new run.
_Avoid_: transaction item (alone), queue item (only one source of Transactions)

**System exception**: REFramework's name for any failure that isn't a business exception; it
covers Process bugs and Environment failures alike, so it is not an Infrastructure failure.

**Business exception**: A `BusinessRuleException`: a business rule the process rejects, an outcome
it expects, not a Failure.

**UiPath action** and **UiPath task**: Action Center's human work items. Neither is a Recovery
action or a Recovery task.

**UiPath trigger**: What starts a job or a handler, such as a UI event trigger or an Orchestrator
event trigger. It is never an Event.
