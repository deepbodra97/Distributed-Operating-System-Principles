#time "on"

#r "nuget: Extreme.Numerics.FSharp"

#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"

open System
open Extreme

open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open Akka.Serialization

// actors will be shutdown automatically after 50s to free the resources
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
            remote {
                helios.tcp {
                    port = 0
                    hostname = 10.0.2.15
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

let system = ActorSystem.Create("Project1Local", configuration) // create an actor systems
let child (childMailbox: Actor<Message>) = // worker actor (child)
    actor {    
        let! msg = childMailbox.Receive() // fetch the message from the queue
        match msg with
        | JobParams(jobParams) -> // if it is a job
            let SumOfConsecutiveSquare (n: Mathematics.BigInteger) =
                n * (n+ Mathematics.BigInteger 1.0) * (Mathematics.BigInteger 2.0 * n + Mathematics.BigInteger 1.0) / Mathematics.BigInteger 6.0
            
            let IsPerfectSquare (n: Mathematics.BigInteger) = // checks if n is a perfect square or not
                let squareRoot = n |> sqrt
                n =  squareRoot * squareRoot

            let ConsecutivePerfectSquareCumulativeSum start stop step = // works on a sub task
                for start in start .. stop do
                    let isPerfectSquare = SumOfConsecutiveSquare(Mathematics.BigInteger (start+step-1.0)) - SumOfConsecutiveSquare(Mathematics.BigInteger (start-1.0)) |> IsPerfectSquare
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

// let args : string array = fsi.CommandLineArgs |> Array.tail
// let n = float args.[0]
// let k = float args.[1]
let nActors = 4.0

let n = 10000000.0
let k = 2.0

let stop = Math.Floor n/2.0
let localParam = JobParams {start=1.0; stop=stop ; step=k; nActors=nActors}
parent <! localParam

let remoteParent = system.ActorSelection("akka.tcp://Project1Remote@172.16.104.242:9001/user/parent")
let remoteParam = JobParams {start=stop+1.0; stop=n ; step=k; nActors=nActors}
remoteParent <! remoteParam
// System.Console.ReadLine()