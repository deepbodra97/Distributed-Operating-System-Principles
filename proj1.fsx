#time "on"

#r "nuget: Extreme.Numerics.FSharp"

#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"

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
        }")

// Job is assigned by sending this message
type JobParams = {
    start: float // start value of n. for this problem it is 1
    stop: float // end value of n. for this problem it is n
    step: float // k
    nActors: float // no of akka actors
}

// different message types
type Message =
    | JobParams of JobParams

// main program
let main start stop step nActors =
    use system = ActorSystem.Create("Project1", configuration) // create an actor systems
    let child (childMailbox: Actor<Message>) = // worker actor (child)
        actor {    
            let! msg = childMailbox.Receive() // fetch the message from the queue
            match msg with
            | JobParams(jobParams) -> // if it is a job
                let SumOfConsecutiveSquare (n: Extreme.Mathematics.BigInteger) =
                    n * (n+ Extreme.Mathematics.BigInteger 1.0) * (Extreme.Mathematics.BigInteger 2.0 * n + Extreme.Mathematics.BigInteger 1.0) / Extreme.Mathematics.BigInteger 6.0
                
                let IsPerfectSquare (n: Extreme.Mathematics.BigInteger) = // checks if n is a perfect square or not
                    let squareRoot = n |> sqrt
                    n =  squareRoot * squareRoot

                let ConsecutivePerfectSquareCumulativeSum start stop step = // works on a sub task
                    for start in start .. stop do
                        let isPerfectSquare = SumOfConsecutiveSquare(Extreme.Mathematics.BigInteger (start+step-1.0)) - SumOfConsecutiveSquare(Extreme.Mathematics.BigInteger (start-1.0)) |> IsPerfectSquare
                        match isPerfectSquare with
                        | true -> printfn "%A" <| bigint start
                        | false -> printf ""
                ConsecutivePerfectSquareCumulativeSum jobParams.start jobParams.stop jobParams.step // perform job
        }

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        match msg with
                        | JobParams jobParams -> // if it is a job
                            let workUnit = (jobParams.stop - jobParams.start + 1.0) / jobParams.nActors |> int |> float
                            let extraWorkUnit = (jobParams.stop - jobParams.start + 1.0) % jobParams.nActors |> int |> float // remainder work unit
                            let mutable tempStart = jobParams.start
                            for i in 1.0 .. jobParams.nActors do
                                let childRef = spawn parentMailbox ("child" + string i) child
                                if i<=extraWorkUnit  then
                                    let childParams = JobParams {start=tempStart ; stop=tempStart+workUnit ; step=jobParams.step; nActors=jobParams.nActors}
                                    tempStart <- tempStart + workUnit + 1.0
                                    childRef <! childParams
                                else
                                    let childParams = JobParams{start=tempStart ; stop=tempStart+workUnit-1.0 ; step=jobParams.step; nActors=jobParams.nActors}
                                    tempStart <- tempStart + workUnit
                                    childRef <! childParams
                        return! parentLoop()
                    }
                parentLoop()
            
            // default supervisor strategy
            <| [ SpawnOption.SupervisorStrategy (
                    Strategy.OneForOne(fun e ->
                    match e with 
                    | _ -> SupervisorStrategy.DefaultDecider.Decide(e)))]

    let jobParams = JobParams {start=start ; stop=stop ; step=step; nActors=nActors}
    parent <! jobParams |> ignore
    system.Terminate()

let args : string array = fsi.CommandLineArgs |> Array.tail
let n = float args.[0]
let k = float args.[1]
let nActors = 8.0
main 1.0 n k nActors