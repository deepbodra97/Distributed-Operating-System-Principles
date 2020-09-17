#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"

open Akka.Actor
open Akka.Configuration
open Akka.FSharp

// actors will be shutdown automatically after 50s to free the resources
let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            coordinated-shutdown {
                phases {    
                    actor-system-terminate {
                        timeout = 50 s
                        depends-on = [before-actor-system-terminate]
                    }
                }
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
            | JobParams(param) -> // if it is a job
                let SumOfConsecutiveSquare n = // sum of n consecutive squares = n * (n+1) * (2n+1) / 6
                    n * (n+1.0) * (2.0 * n + 1.0) / 6.0
                
                let IsPerfectSquare n = // checks if n is a perfect square or not
                    let flooredSquareRoot = n |> double |> sqrt |> floor |> int
                    n =  flooredSquareRoot * flooredSquareRoot // perfect square if floored square root is equal to n

                let ConsecutivePerfectSquareCumulativeSum start stop step = // works on a sub task
                    for start in [start .. stop] do
                        let isPerfectSquare = SumOfConsecutiveSquare(start+step-1.0) - SumOfConsecutiveSquare(start-1.0) |> int |> IsPerfectSquare
                        match isPerfectSquare with
                        | true -> printfn "%d" <| int start
                        | false -> printf ""
                ConsecutivePerfectSquareCumulativeSum param.start param.stop param.step // perform job
        }

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        match msg with
                        | JobParams param -> // if it is a job
                            let workUnit = (param.stop - param.start + 1.0) / param.nActors |> int |> float
                            let extraWorkUnit = (param.stop - param.start + 1.0) % param.nActors |> int |> float // remainder work unit
                            let mutable tempStart = param.start
                            for i in [1.0 .. param.nActors] do
                                let childRef = spawn parentMailbox ("child" + string i) child
                                if i<=extraWorkUnit  then
                                    let childParams = JobParams {start=tempStart ; stop=tempStart+workUnit ; step=param.step; nActors=param.nActors}
                                    tempStart <- tempStart + workUnit + 1.0
                                    childRef.Forward(childParams)
                                else
                                    let childParams = JobParams{start=tempStart ; stop=tempStart+workUnit-1.0 ; step=param.step; nActors=param.nActors}
                                    tempStart <- tempStart + workUnit
                                    childRef.Forward(childParams)
                        return! parentLoop()
                    }
                parentLoop()
            
            // default supervisor strategy
            <| [ SpawnOption.SupervisorStrategy (
                    Strategy.OneForOne(fun e ->
                    match e with 
                    | _ -> SupervisorStrategy.DefaultDecider.Decide(e)))  ]

    let param = JobParams {start=start ; stop=stop ; step=step; nActors=nActors}
    parent <! param |> ignore
    system.Terminate()

let args : string array = fsi.CommandLineArgs |> Array.tail
let n = float args.[0]
let k = float args.[1]
let nActors = 8.0
main 1.0 n k nActors