#r "nuget: Akka.FSharp"

open Akka.Actor
open Akka.FSharp
open System
open System.Diagnostics

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
let main numNodes =
    use system = ActorSystem.Create("Project3") // create an actor system

    let getRandomInt start stop =  // get random integer [start, stop]
        let rnd = System.Random()
        rnd.Next(start, stop+1)

    let child (childMailbox: Actor<_>) = // pastry node
        let id = childMailbox.Self.Path.Name // id (hexadecimal)
        let mutable cancelable = Unchecked.defaultof<ICancelable> // to cancel making requests

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | _ -> return ()
                return! childLoop()
            }

        childLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let mutable mainSender = Unchecked.defaultof<IActorRef> // main program
                
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Start start -> // spawn pastry nodes one by one
                            printfn "Join phase: Please wait for all the nodes to join the network"
                            mainSender <- sender
                            // for i in 1 .. numNodes do
                            
                        | _ -> return ()
                        return! parentLoop()
                    }
                parentLoop()

            // default supervisor strategy
            <| [ SpawnOption.SupervisorStrategy (
                    Strategy.OneForOne(fun e ->
                    match e with 
                    | _ -> SupervisorStrategy.DefaultDecider.Decide(e)))]


    async {
        let! response = parent <? Start "0"
        printfn ""
    } |> Async.RunSynchronously

let args : string array = fsi.CommandLineArgs |> Array.tail
let numNodes = int args.[0]
main numNodes