#r "nuget: Akka.FSharp"

open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open Akka.Serialization
open System
open System.Diagnostics

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            actor{
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                serializers {
                    hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                }
                serialization-bindings {
                    ""System.Object"" = hyperion
                }
            }
            remote.helios.tcp {
                hostname = 127.0.0.1
                port = 9001
            }
        }")

// different message types
// type Message =
    // | Start of string // parent starts spawning nodes. Nodes start joining
    // | StartRequestPhase // Nodes start making 1 request per second
    // | Join of string // route the Join packet
    // | JoinSuccess // parent know that a node has finished joining
    // | NewRow of int * string[] //  (row number, row of routing table)
    // | NewLeaves of Set<string> // leaf set
    // | Route of string * string * int // route the Route(request) packet
    // | RouteSuccess of int // report number of hops to parent
    // | RequestTick // tick every 1 second

// main program
let main () =
    use system = ActorSystem.Create("Twitter") // create an actor system

    let server =
        spawn system "server"
            <| fun mailbox ->
                let id = mailbox.Self.Path.Name // id

                let rec loop() =
                    actor {   
                        let! msg = mailbox.Receive() // fetch the message from the queue
                        let sender = mailbox.Sender()
                        match msg with
                        | _ -> return ()
                        return! loop()
                    }
                loop()


    async {
        let! response = server <? ()
        printfn ""
    } |> Async.RunSynchronously


main ()