#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: Akka.TestKit"

open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
// open Akka.TestKit

let configuration = 
    ConfigurationFactory.ParseString(
        @"akka {
            // log-config-on-start : on
            // stdout-loglevel : DEBUG
            // loglevel : ERROR
            actor {
                provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                debug : {
                    receive : on
                    autoreceive : on
                    lifecycle : on
                    event-stream : on
                    unhandled : on
                }
            }
            remote {
                helios.tcp {
                    port = 8777
                    hostname = localhost
                }
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


type JobParams = {
    start: float
    stop: float
    step: float
    nActors: float
}

type Message =
    | JobParams of JobParams

let main() =
    use system = ActorSystem.Create("Project1", configuration)
    let child (childMailbox: Actor<Message>) = 
        actor {    
            let! msg = childMailbox.Receive()
            match msg with
            | JobParams(param) ->
                let SumOfConsecutiveSquare n =
                    n * (n+1.0) * (2.0 * n + 1.0) / 6.0
                
                let IsPerfectSquare n =
                    let flooredSquare = n |> double |> sqrt |> floor |> int
                    n =  flooredSquare * flooredSquare

                let ConsecutivePerfectSquareCumulativeSum start stop step =
                    for start in [start .. stop] do
                        let isPerfectSquare = SumOfConsecutiveSquare(start+step-1.0) - SumOfConsecutiveSquare(start-1.0) |> int |> IsPerfectSquare
                        match isPerfectSquare with
                        | true -> printfn "%d" <| int start
                        | false -> printf ""
                ConsecutivePerfectSquareCumulativeSum param.start param.stop param.step
        }

    let parent = 
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive()
                        match msg with
                        | JobParams param ->
                            let workUnit = (param.stop - param.start + 1.0) / param.nActors |> int |> float
                            let extraWorkUnit = (param.stop - param.start + 1.0) % param.nActors |> int |> float
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
            
            <| [ SpawnOption.SupervisorStrategy (
                    Strategy.OneForOne(fun e ->
                    match e with 
                    | _ -> SupervisorStrategy.DefaultDecider.Decide(e)))  ]

    // async {
        // let param = JobParams {start=1.0 ; stop=40.0 ; step=24.0; nActors=4.0}
        // let param = JobParams {start=1.0 ; stop=1000000.0 ; step=24.0; nActors=4.0}
        // let param = JobParams {start=1.0 ; stop=100000000.0 ; step=2.0; nActors=4.0}
        // let param = JobParams {start=1.0 ; stop=100000000000.0 ; step=24.0; nActors=10.0}
        // parent <! param
        // system.Terminate()
    // } |> Async.RunSynchronously |> ignore
    let param = JobParams {start=1.0 ; stop=100000000.0 ; step=2.0; nActors=4.0}
    // let param = JobParams {start=1.0 ; stop=100000000.0 ; step=20.0; nActors=8.0}
    parent <! param |> ignore
    system.Terminate()

main ()