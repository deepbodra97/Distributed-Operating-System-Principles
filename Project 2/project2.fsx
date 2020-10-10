#time "on"

#r "nuget: Extreme.Numerics.FSharp"

#r "nuget: Akka.FSharp"

open Extreme

open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System

// actor system will not shutdown before actors have completed executing
let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            coordinated-shutdown {
                terminate-actor-system = off
                run-by-actor-system-terminate = off
            }
        }"
    )

// Job is assigned by sending this message

// different message types

type Message =
    | Rumor of string
    | Done of string

// main program
let main numNodes =
    use system = ActorSystem.Create("Project2", configuration) // create an actor systems
    let child (childMailbox: Actor<_>) = // worker actor (child)
        let getRandomNeighbor (neighbors: _[]) =  
            let rnd = System.Random()
            neighbors.[rnd.Next(neighbors.Length)]

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                printfn "Sender: %O" sender
                match msg with
                | Rumor rumor -> // if it is a job
                    // printfn "Child received rumor %O %s" <| childMailbox.Self.Path.ToStringWithAddress() <| rumor
                    let randomNeighborName = "child" +  string(getRandomNeighbor([|1 .. numNodes|]))
                    let randomNeighbor = system.ActorSelection("akka://Project2/user/parent/"+randomNeighborName)
                    randomNeighbor <! Rumor rumor
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
                                let childRef = spawn parentMailbox ("child" + string i) child
                                childRef <! Rumor rumor
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

    let rumor = Rumor "I Love Distrubuted Systems"
    parent <! rumor
    system.Terminate()

// let args : string array = fsi.CommandLineArgs |> Array.tail
// let n = float args.[0]
// let k = float args.[1]
// let nActors = 8.0
main 10