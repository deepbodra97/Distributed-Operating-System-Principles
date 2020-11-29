#r "nuget: Akka.FSharp"

open Akka.Actor
open Akka.FSharp
open System
open System.Diagnostics

// different message types
type Message =
    | Rumor of string
    | TickRumor of string
    | ValueWeightInit of float * float
    | PushSum of float * float
    | TickPushSum of string
    | Done of float
    | Fail of string

// main program
let main n topology algorithm nFail =
    use system = ActorSystem.Create("Project2") // create an actor systems

    let getRandomInt start stop =  // get random integer [start, stop]
            let rnd = System.Random()
            rnd.Next(start, stop+1)
    let dim =  float n |> sqrt |> floor |> int // dimension for 2D and imperfect 2D

    let numNodes = // calculate number of nodes for 2D and imp2D as the nearest perfect square to n
        match topology with
        | "2D" | "imp2D" -> dim * dim
        | _ -> n

    let child (childMailbox: Actor<_>) = // nodes participating in gossip or pushsum
        let id = childMailbox.Self.Path.Name |> int // id
        let mutable messageCount = 0 // number of messages received so far (convergence for gossip)
        let randomNeighbor = getRandomInt 1 numNodes // random fixed neighbor for imp2D

        let mutable cancelable = Unchecked.defaultof<ICancelable> // handler to the round tick of the gossip and pushsum
        let mutable value = 0.0 // x
        let mutable weight = 0.0 // w
        let mutable consecutive = 0 // convergence for pushsum when conescitive=5
        let mutable hasConverged = false // true if a node has converged
        let mutable isActive = true

        // For 2D and imp2D
        let getLeftNeighbor = if id % dim = 1 then -1 else (id-1) // left neighbor id for 2D and imp2D
        let getRightNeighbor = if id % dim = 0 then -1 else (id+1) // right neighbor id for 2D and imp2D

        let chooseNeighbor () = // chooses a random neighbor
            match topology with
            | "line" ->
                let neighbors = [| id-1; id+1 |] // left, right
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 0 (neighbors.Length-1)])
                randomNeighborName
            | "2D" ->
                let neighbors = [|id-dim; getLeftNeighbor; getRightNeighbor; id+dim|] // top, left, right, bottom
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 0 (neighbors.Length-1)])
                randomNeighborName
            | "imp2D" ->
                let neighbors = [|id-dim; getLeftNeighbor; getRightNeighbor; id+dim; randomNeighbor|] // top, left, right, bottom, random
                                |> Array.filter (fun x -> x>=1 && x<=numNodes)
                let randomNeighborName = string(neighbors.[getRandomInt 0 (neighbors.Length-1)])
                randomNeighborName
            | "full" ->
                let randomNeighborName = string(getRandomInt 1 numNodes) // pick one from everyone
                randomNeighborName

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Rumor rumor -> // if it is a gossip
                    if isActive then    
                        if not hasConverged then    
                                if messageCount = 0 then
                                    cancelable <- system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(300.0)), childMailbox.Self, (TickRumor(rumor)), childMailbox.Self)
                                messageCount <- messageCount + 1
                                if messageCount = 10 then
                                    cancelable.Cancel ()
                                    hasConverged <- true
                                    system.ActorSelection("/user/parent") <! Done 1.0
                        else                       
                            system.ActorSelection("/user/parent/"+chooseNeighbor ()) <! Rumor rumor
                | TickRumor rumor -> // if it is a tick for gossip
                    if isActive then    
                        system.ActorSelection("/user/parent/"+chooseNeighbor ()) <! Rumor rumor
                | ValueWeightInit (v, w) -> // to initialise the pair (x, w)
                    value <- v
                    weight <- w
                | PushSum (newValue, newWeight) -> // if it is a pushsum
                    if isActive then    
                        if not hasConverged then    
                            if messageCount = 0 then
                                cancelable <- system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(1.0)), childMailbox.Self, (TickPushSum "tick"), childMailbox.Self)
                            messageCount <- messageCount + 1
                            if abs (value/weight - (value+newValue)/(weight+newWeight)) < 10.0**(-10.0) then
                                consecutive <- consecutive + 1
                                if consecutive = 5 then // node converged
                                    hasConverged <- true
                                    system.ActorSelection("/user/parent") <! Done (value/weight) // tell parent that I have converged
                            else
                                consecutive <- 0
                            value <- value + newValue // update x
                            weight <- weight + newWeight // update w
                | TickPushSum tick -> // if it is a tick for pushsum
                    if isActive then    
                        value <- value / 2.0
                        weight <- weight / 2.0
                        system.ActorSelection("/user/parent/"+chooseNeighbor ()) <! PushSum(value, weight) // send 1/2 to a random neighbor
                | Fail fail ->
                    isActive <- false
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
                        | Rumor rumor ->
                            printfn "Parent received rumor %s" rumor
                            mainSender <- sender
                            for i in 1 .. numNodes do
                                spawn parentMailbox (string i) child |> ignore
                            for i in 1 .. nFail do
                                system.ActorSelection("/user/parent/"+ string(getRandomInt 1 numNodes)) <! Fail "fail"   
                            system.ActorSelection("/user/parent/"+ string(getRandomInt 1 numNodes)) <! Rumor rumor // start gossip
                        | PushSum (s, w) ->
                            mainSender <- sender
                            for i in 1 .. numNodes do
                                let childRef = spawn parentMailbox (string i) child
                                childRef <! ValueWeightInit (float i, 0.0)
                            for i in 1 .. nFail do
                                system.ActorSelection("/user/parent/"+ string(getRandomInt 1 numNodes)) <! Fail "fail"  
                            let startId = getRandomInt 1 numNodes // pick random start id
                            let startRef = system.ActorSelection("/user/parent/"+ string(startId)) // select that node
                            startRef <! ValueWeightInit (float startId, 1.0) // initialise x and w for that node
                            startRef <! PushSum(s, w) // start pushsum
                        | Done done_msg ->
                            match algorithm with
                            | "gossip" -> printfn "Node %s converged. Total Nodes Converged: %d" sender.Path.Name (messageCount+1)
                            | "pushsum" -> printfn "Node %s converged to %f. Total Nodes Converged: %d" sender.Path.Name done_msg (messageCount+1)
                            messageCount <- messageCount + 1 
                            if messageCount = (numNodes-nFail) then // if all nodes have converged
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
        printfn "Convergence Time: %s ms" <| timer.ElapsedMilliseconds.ToString()
            
    } |> Async.RunSynchronously

let args : string array = fsi.CommandLineArgs |> Array.tail
let n = int args.[0]
let topology = args.[1]
let algorithm = args.[2]
let nFail = int args.[3]
main n topology algorithm nFail

// tests
// main 100 "line" "gossip" 0
// main 100 "line" "gossip" 5
// main 100 "line" "gossip" 10
// main 100 "line" "gossip" 20

// main 100 "2D" "gossip" 0
// main 100 "2D" "gossip" 5
// main 100 "2D" "gossip" 10
// main 100 "2D" "gossip" 20
// main 100 "2D" "gossip" 30
// main 100 "2D" "gossip" 35

// main 100 "imp2D" "gossip" 0
// main 100 "imp2D" "gossip" 5
// main 100 "imp2D" "gossip" 10
// main 100 "imp2D" "gossip" 20
// main 100 "imp2D" "gossip" 30
// main 100 "imp2D" "gossip" 35
// main 100 "imp2D" "gossip" 40
// main 100 "imp2D" "gossip" 50
// main 100 "imp2D" "gossip" 60


// main 100 "full" "gossip" 0
// main 100 "full" "gossip" 5
// main 100 "full" "gossip" 10
// main 100 "full" "gossip" 20
// main 100 "full" "gossip" 30
// main 100 "full" "gossip" 40
// main 100 "full" "gossip" 50
// main 100 "full" "gossip" 60
// main 100 "full" "gossip"  70
// main 100 "full" "gossip"  80
// main 100 "full" "gossip"  90
// main 100 "full" "gossip" 99




// -------------

// main 10 "line" "pushsum"
// main 50 "line" "pushsum"
// main 100 "line" "pushsum"
// main 500 "line" "pushsum"
// main 1000 "line" "pushsum"
// main 5000 "line" "pushsum"
// main 10000 "line" "pushsum"

// main 10 "2D" "pushsum"
// main 50 "2D" "pushsum"
// main 100 "2D" "pushsum"
// main 500 "2D" "pushsum"
// main 1000 "2D" "pushsum"
// main 5000 "2D" "pushsum"
// main 10000 "2D" "pushsum"

// main 10 "imp2D" "pushsum"
// main 50 "imp2D" "pushsum"
// main 100 "imp2D" "pushsum"
// main 500 "imp2D" "pushsum"
// main 1000 "imp2D" "pushsum"
// main 5000 "imp2D" "pushsum"
// main 10000 "imp2D" "pushsum"

// main 10 "full" "pushsum" 2
// main 50 "full" "pushsum"
// main 100 "full" "pushsum"
// main 500 "full" "pushsum"
// main 1000 "full" "pushsum"
// main 5000 "full" "pushsum"
// main 10000 "full" "pushsum"