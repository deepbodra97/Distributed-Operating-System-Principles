#time "on"

#r "nuget: Extreme.Numerics.FSharp"

#r "nuget: Akka.FSharp"

open Extreme

open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open System

// actor system will not shutdown before actors have completed executing
// let configuration = 
//     ConfigurationFactory.ParseString(
//         @"akka {
//             coordinated-shutdown {
//                 terminate-actor-system = off
//                 run-by-actor-system-terminate = off
//             }
//         }"
//     )

// Job is assigned by sending this message

// different message types

type Message =
    | Rumor of string
    | Done of string

// main program
let main numNodes =
    use system = ActorSystem.Create("Project2") // create an actor systems
    let child (childMailbox: Actor<Message>) =
        let getRandomNeighbor (neighbors: _[]) =  
            let rnd = System.Random()
            neighbors.[rnd.Next(neighbors.Length)]

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Rumor rumor -> // if it is a job
                    printfn "Child received rumor %O %s" <| childMailbox.Self.Path.ToStringWithAddress() <| rumor
                    let randomNeighborName = string(getRandomNeighbor([|1 .. numNodes|]))
                    // printfn "Random neighbor: %s" randomNeighborName
                    let randomNeighbor = system.ActorSelection("akka://Project2/user/"+randomNeighborName)
                    randomNeighbor <! Rumor rumor
                | _ -> printfn "Invalid message"
                return! childLoop()
            }
        childLoop()

    let rumor = Rumor "I Love Distrubuted Systems"
    for i in 1 .. numNodes do
        let ref = spawn system (string i) child
        printfn "created %O" ref
    system.ActorSelection("akka://Project2/user/1") <! rumor
    System.Console.ReadLine()
    // system.Terminate()

// let args : string array = fsi.CommandLineArgs |> Array.tail
// let n = float args.[0]
// let k = float args.[1]
// let nActors = 8.0
main 10