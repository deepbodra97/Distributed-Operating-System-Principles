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
let main n topology algorithm =
    use system = ActorSystem.Create("Project2") // create an actor systems

    let getRandomInt start stop =  
            let rnd = System.Random()
            rnd.Next(start, stop+1)
    let dim =  float n |> sqrt |> floor |> int

    let numNodes =
        match topology with
        | "2D" | "imp2D" -> dim * dim
        | _ -> n

    let child (childMailbox: Actor<_>) = // worker actor (child)
        let id = childMailbox.Self.Path.Name |> int
        let mutable gossipCount = 0
        let randomNeighbor = getRandomInt 1 numNodes+1

        // For 2D and imp2D
        let getLeftNeighbor = if id % dim = 1 then -1 else (id-1)
        let getRightNeighbor = if id % dim = 0 then -1 else (id+1)

        let chooseNeighbor () =
            match topology with
            | "line" ->
                let neighbors = [| id-1; id+1 |]
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 1 neighbors.Length-1])
                randomNeighborName
            | "2D" ->
                let neighbors = [|id-dim; getLeftNeighbor; getRightNeighbor; id+dim|]
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 1 neighbors.Length-1])
                randomNeighborName
            | "imp2D" ->
                let neighbors = [|id-dim; getLeftNeighbor; getRightNeighbor; id+dim; randomNeighbor|]
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 1 neighbors.Length-1])
                randomNeighborName
            | "full" ->
                let randomNeighborName = string(getRandomInt 1 numNodes)
                randomNeighborName

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Rumor rumor -> // if it is a job
                    if gossipCount = 0 then
                        system.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, (TimeSpan.FromMilliseconds(1.0)), childMailbox.Self, (TickRumor(rumor)))

                    gossipCount <- gossipCount + 1
                    if gossipCount = 10 then
                        system.ActorSelection("/user/parent") <! Done "done"
                | TickRumor rumor ->
                    system.ActorSelection("/user/parent/"+chooseNeighbor ()) <! Rumor rumor
                | _ -> printfn "Invalid message"
                return! childLoop()
            }
        childLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let mutable gossipCount = 0
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Rumor rumor -> // if it is a job
                            printfn "Parent received rumor %s" rumor
                            for i in 1 .. numNodes do
                                spawn parentMailbox (string i) child |> ignore
                            system.ActorSelection("/user/parent/"+ string(getRandomInt 1 numNodes)) <! Rumor rumor
                        | Done done_msg ->
                            printfn "Parent received done %O" sender
                        
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
main 10 "line" "gossip"
// main 1000 "2D" "gossip"
// main 5 "imp2D" "gossip"
// main 1000 "full" "gossip"