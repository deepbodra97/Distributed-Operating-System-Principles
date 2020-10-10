#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"

open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open Akka.Serialization

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
                hostname = 172.16.104.242
                port = 9001
            }
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
let system = ActorSystem.Create("Project1Remote", configuration) // create an actor systems
let child (childMailbox: Actor<Message>) = // worker actor (child)
    actor {    
        let! msg = childMailbox.Receive() // fetch the message from the queue
        match msg with
        | JobParams(jobParams) -> // if it is a job
            let SumOfConsecutiveSquare n = // sum of n consecutive squares = n * (n+1) * (2n+1) / 6
                n * (n+1.0) * (2.0 * n + 1.0) / 6.0

            let IsPerfectSquare n = // checks if n is a perfect square or not
                let flooredSquareRoot = n |> float |> sqrt |> floor
                n =  flooredSquareRoot * flooredSquareRoot // perfect square if floored square root is equal to n

            let ConsecutivePerfectSquareCumulativeSum start stop step = // works on a sub task
                for start in start .. stop do
                    let isPerfectSquare = SumOfConsecutiveSquare(start+step-1.0) - SumOfConsecutiveSquare(start-1.0) |> IsPerfectSquare
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
                | _ -> SupervisorStrategy.DefaultDecider.Decide(e))) ]
// System.Console.ReadLine() 