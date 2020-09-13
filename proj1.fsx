#time "on"
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.TestKit" 

open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
// open Akka.TestKit

// let system = ActorSystem.Create("Project1")

// let echoServer = 
//     spawn system "EchoServer"
//     <| fun mailbox ->
//             actor {
//                 let! message = mailbox.Receive()

//                 let SumOfConsecutiveSquare n =
//                     n * (n+1.0) * (2.0 * n + 1.0) / 6.0

//                 let IsPerfectSquare n =
//                     let flooredSquare = n |> double |> sqrt |> floor |> int
//                     n =  flooredSquare * flooredSquare

//                 let ConsecutivePerfectSquareCumulativeSum n k =
//                     for start in [1.0 .. n] do
//                         let isPerfectSquare = SumOfConsecutiveSquare(start+k-1.0) - SumOfConsecutiveSquare(start-1.0) |> int |> IsPerfectSquare
//                         match isPerfectSquare with
//                         | true -> printfn "%d" <| int start
//                         | false -> printf ""
//                 ConsecutivePerfectSquareCumulativeSum 40.0 24.0 
//             }

type CustomException() =
    inherit Exception()


type JobParams = {
    start: float
    stop: float
    step: float
    nActors: float
}

type Message =
    | JobParams of JobParams
    | Crash

let main() =
    use system = ActorSystem.Create("Project1")
    let child (childMailbox: Actor<Message>) = 
        actor {    
            let! msg = childMailbox.Receive()
            printfn "Child received a message"
            match msg with
            | JobParams(param) ->
                let response = "Child " + (childMailbox.Self.Path.ToStringWithAddress()) + " received: " + string param.start + " " + string param.stop + " " + string param.step
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
                // printfn "%s" response
                // childMailbox.Sender() <! response
            | Crash -> 
                printfn "Child %A received crash order" (childMailbox.Self.Path)
                raise (CustomException())
        }

    let parent = 
        spawnOpt system "parent"
            <| fun parentMailbox ->
                // define parent behavior
                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive()
                        printfn "Parent received a message"
                        match msg with
                        | JobParams param ->
                            printfn "Parent received JobParams"
                            let workUnit = (param.stop - param.start + 1.0) / param.nActors |> int |> float
                            let extraWorkUnit = (param.stop - param.start + 1.0) % param.nActors |> int |> float
                            let mutable tempStart = param.start
                            for i in [1.0 .. param.nActors] do
                                let childRef = spawn parentMailbox ("child" + string i) child
                                printfn "%O" childRef
                                if i<=extraWorkUnit  then
                                    let childParams = JobParams {start=tempStart ; stop=tempStart+workUnit ; step=param.step; nActors=param.nActors}
                                    tempStart <- tempStart + workUnit + 1.0
                                    childRef.Forward(childParams)
                                else
                                    let childParams = JobParams{start=tempStart ; stop=tempStart+workUnit-1.0 ; step=param.step; nActors=param.nActors}
                                    tempStart <- tempStart + workUnit
                                    childRef.Forward(childParams)
                            // let childRef = spawn parentMailbox ("child" + string 1) child
                            // childRef.Forward(msg)  // forward all messages through
                        return! parentLoop()
                    }
                parentLoop()
            // define supervision strategy
            <| [ SpawnOption.SupervisorStrategy (
                    // restart on Custom Exception, default behavior on all other exception types
                    Strategy.OneForOne(fun e ->
                    match e with
                    | :? CustomException -> Directive.Restart 
                    | _ -> SupervisorStrategy.DefaultDecider.Decide(e)))  ]

    async {
        let param = JobParams {start=1.0 ; stop=40.0 ; step=24.0; nActors=2.0}
        let! response = parent <? param
        printfn "%s" response
        system.Terminate()
    } |> Async.RunSynchronously |> ignore
    // let param = {start=1.0 ; stop=40.0 ; step=24.0; nActors=2.0}
    // parent <? JobParams param |> ignore


main ()