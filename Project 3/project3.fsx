#r "nuget: Akka.FSharp"

open Akka.Actor
open Akka.FSharp
open System
open System.Diagnostics

// different message types
type Message =
    | Start of string

// main program
let main numNodes numRequests =
    use system = ActorSystem.Create("Project3") // create an actor systems
    let getRandomInt start stop =  // get random integer [start, stop]
        let rnd = System.Random()
        rnd.Next(start, stop+1)

    let changeBase (x: int) (b: int) =
        System.Convert.ToString(x, b).PadLeft(7, '0')
    
    let child (childMailbox: Actor<_>) = // nodes participating in gossip or pushsum
        let id = childMailbox.Self.Path.Name // id

        let mutable largerLeaves = "1111111"
        let mutable smallerLeaves = "0000000"

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Start start ->
                    printfn "Child %s's coordinator is %s" id start
                    if start <> "0" then
                        printfn ""
                | _ -> return ()
                return! childLoop()
            }

        childLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                
                let activeNodes = Array.create numNodes ""

                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Start start ->
                            printfn "Parent received start"
                            for i in 1 .. numNodes do
                                let id = changeBase (getRandomInt 1 100) 2
                                activeNodes.[i-1] <- id
                                let childRef = spawn parentMailbox id child
                                if i = 1 then
                                    childRef <! Start "0"
                                else
                                    let poc = activeNodes.[getRandomInt 0 (i-2)] 
                                    childRef <! Start poc
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
        // timer.Start()
        let! response = parent <? Start "0"
        // printfn "Convergence Time: %s ms" <| timer.ElapsedMilliseconds.ToString()
        printfn "Exit"
    } |> Async.RunSynchronously

// let args : string array = fsi.CommandLineArgs |> Array.tail
// let n = int args.[0]
// let topology = args.[1]
// let algorithm = args.[2]
main 5 10