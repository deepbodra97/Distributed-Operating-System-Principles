#time "on"

#r "nuget: Extreme.Numerics.FSharp"

#r "nuget: Akka.FSharp"

open Extreme

open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System

// Job is assigned by sending this message

// different message types

type Message =
    | Rumor of string
    | TickRumor of string
    | Done of string

// main program
let main numNodes =
    use system = ActorSystem.Create("Project2") // create an actor systems
    let child (childMailbox: Actor<_>) = // worker actor (child)
        let getRandomNeighbor (neighbors: _[]) =  
            let rnd = System.Random()
            neighbors.[rnd.Next(neighbors.Length)]

        let mutable gossipCount = 0

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Rumor rumor -> // if it is a job
                    if gossipCount = 0 then
                        system.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, (TimeSpan.FromMilliseconds(100.0)), childMailbox.Self, (TickRumor(rumor)))

                    gossipCount <- gossipCount + 1
                    if gossipCount = 10 then
                        system.ActorSelection("/user/parent") <! Done "done"
                | TickRumor rumor ->
                    let randomNeighborName = "child" +  string(getRandomNeighbor([|1 .. numNodes|]))
                    system.ActorSelection("/user/parent/"+randomNeighborName) <! Rumor rumor
                | _ -> printfn "Invalid message"
                return! childLoop()
            }
        childLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        match msg with
                        | Rumor rumor -> // if it is a job
                            printfn "Parent received rumor %s" rumor
                            for i in 1 .. numNodes do
                                spawn parentMailbox ("child" + string i) child |> ignore
                            system.ActorSelection("/user/parent/child1") <! Rumor rumor
                        | Done done_msg ->
                            printfn "Parent received done %s" done_msg
                        return! parentLoop()
                    }
                parentLoop()
            
            // default supervisor strategy
            <| [ SpawnOption.SupervisorStrategy (
                    Strategy.OneForOne(fun e ->
                    match e with 
                    | _ -> SupervisorStrategy.DefaultDecider.Decide(e)))]


    async {
        let rumor = Rumor "I Love Distrubuted Systems"
        let! response = parent <? rumor
        printfn "%s" response    
    } |> Async.RunSynchronously

// let args : string array = fsi.CommandLineArgs |> Array.tail
// let n = float args.[0]
// let k = float args.[1]
// let nActors = 8.0
main 10