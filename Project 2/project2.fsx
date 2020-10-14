#r "nuget: Akka.FSharp"

open Akka.Actor
// open Akka.Configuration
open Akka.FSharp
open System
open System.Diagnostics

// Job is assigned by sending this message

// different message types
type Message =
    | Rumor of string
    | TickRumor of string
    | ValueWeightInit of float * float
    | PushSum of float * float
    | TickPushSum of string
    | Done of float

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
        let mutable messageCount = 0
        let randomNeighbor = getRandomInt 1 numNodes

        let mutable cancelable = Unchecked.defaultof<ICancelable>
        let mutable value = 0.0
        let mutable weight = 0.0
        let mutable consecutive = 0
        let mutable hasConverged = false

        // For 2D and imp2D
        let getLeftNeighbor = if id % dim = 1 then -1 else (id-1)
        let getRightNeighbor = if id % dim = 0 then -1 else (id+1)

        let chooseNeighbor () =
            match topology with
            | "line" ->
                let neighbors = [| id-1; id+1 |]
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 0 (neighbors.Length-1)])
                randomNeighborName
            | "2D" ->
                let neighbors = [|id-dim; getLeftNeighbor; getRightNeighbor; id+dim|]
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 0 (neighbors.Length-1)])
                randomNeighborName
            | "imp2D" ->
                let neighbors = [|id-dim; getLeftNeighbor; getRightNeighbor; id+dim; randomNeighbor|]
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 0 (neighbors.Length-1)])
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
                    if messageCount = 0 then
                        cancelable <- system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(1.0)), childMailbox.Self, (TickRumor(rumor)), childMailbox.Self)
                    messageCount <- messageCount + 1
                    if messageCount = 10 then
                        system.ActorSelection("/user/parent") <! Done 1.0
                | TickRumor rumor ->
                    system.ActorSelection("/user/parent/"+chooseNeighbor ()) <! Rumor rumor
                | ValueWeightInit (v, w) ->
                    value <- v
                    weight <- w
                | PushSum (newValue, newWeight) ->
                    if not hasConverged then    
                        if messageCount = 0 then
                            cancelable <- system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(0.1)), childMailbox.Self, (TickPushSum "tick"), childMailbox.Self)
                        messageCount <- messageCount + 1
                        if abs (value/weight - (value+newValue)/(weight+newWeight)) < 10.0**(-10.0) then
                            consecutive <- consecutive + 1
                            if consecutive = 5 then
                                // printfn "Converged: %f" (value/weight)
                                // cancelable.Cancel ()
                                hasConverged <- true
                                system.ActorSelection("/user/parent") <! Done (value/weight)
                        else
                            consecutive <- 0

                        value <- value + newValue
                        weight <- weight + newWeight
                    // else
                        // value <- value + newValue
                        // weight <- weight + newWeight
                        // value <- value / 2.0
                        // weight <- weight / 2.0
                        // system.ActorSelection("/user/parent/"+chooseNeighbor ()) <! PushSum(newValue, newWeight)

                | TickPushSum tick ->
                    value <- value / 2.0
                    weight <- weight / 2.0
                    system.ActorSelection("/user/parent/"+chooseNeighbor ()) <! PushSum(value, weight)
                    // printfn "Sum=%f" (value/weight)
                | _ -> printfn "Invalid message"
                return! childLoop()
            }

        childLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let mutable mainSender = Unchecked.defaultof<IActorRef>
                let mutable messageCount = 0
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Rumor rumor -> // if it is a job
                            printfn "Parent received rumor %s" rumor
                            mainSender <- sender
                            for i in 1 .. numNodes do
                                spawn parentMailbox (string i) child |> ignore
                            system.ActorSelection("/user/parent/"+ string(getRandomInt 1 numNodes)) <! Rumor rumor
                        | PushSum (s, w) ->
                            printfn "Parent received push sum %f %f" s w
                            mainSender <- sender
                            for i in 1 .. numNodes do
                                let childRef = spawn parentMailbox (string i) child
                                childRef <! ValueWeightInit (float i, 0.0)
                            let startId = getRandomInt 1 numNodes
                            let startRef = system.ActorSelection("/user/parent/"+ string(startId))
                            startRef <! ValueWeightInit (float startId, 1.0)
                            startRef <! PushSum(s, w)
                        | Done done_msg ->
                            match algorithm with
                            | "gossip" -> printfn "Node %s converged" sender.Path.Name
                            | "pushsum" -> printfn "Node %s converged to %f" sender.Path.Name done_msg
                            messageCount <- messageCount + 1
                            if messageCount = numNodes then
                                system.Terminate() |> ignore
                                mainSender <! "Done"
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
        let timer = new Stopwatch()
        timer.Start()
        match algorithm  with
        | "gossip" ->
            let rumor = Rumor "I Love Distrubuted Systems"
            let! response = parent <? rumor
            timer.Stop()
        | "pushsum" ->
            let pushsum = PushSum(0.0, 0.0)
            let! response = parent <? pushsum
            timer.Stop()
        // System.Console.WriteLine("Time elapsed: {0}", timer.ElapsedMilliseconds)
        printfn "Convergence Time: %s ms" <| timer.ElapsedMilliseconds.ToString()
            
    } |> Async.RunSynchronously

let args : string array = fsi.CommandLineArgs |> Array.tail
let n = int args.[0]
let topology = args.[1]
let algorithm = args.[2]
main n topology algorithm

// main 5 "line" "gossip"
// main 100 "line" "gossip"

// main 1000 "2D" "gossip"
// main 5 "imp2D" "gossip"
// main 1000 "full" "gossip"

// main 5 "line" "pushsum" // 15=15
// main 20 "line" "pushsum" // 210
// main 100 "line" "pushsum" // 5050=5050



// 2D works
// main 121 "2D" "pushsum" // 7381=7381
// main 961 "2D" "pushsum" // wrong=462241

// imp2D works
// main 16 "imp2D" "pushsum" // 136=136
// main 121 "imp2D" "pushsum" // 7381=7381
// main 961 "imp2D" "pushsum" // 462241=462241

// full works
// main 5 "full" "pushsum" // 15
// main 100 "full" "pushsum" // 5050
// main 1000 "full" "pushsum" // 500500
// main 10000 "full" "pushsum" // 50005000