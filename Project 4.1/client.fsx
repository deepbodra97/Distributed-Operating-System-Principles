#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"
open Akka.Actor
open Akka.FSharp
open Akka.Configuration
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
            remote {
                helios.tcp {
                    port = 9002
                    hostname = 127.0.0.1
                }
            }
        }")

type User = {
    id: string
    username: string
    password: string
}

type Tweet = {
    id: string
    reId: string
    text: string
    tType: string
    by: User
}

type Query = {
    qType: string
    qName: string
    by: User
}

type Subscribe = {
    publisher: string
    subscriber: string
}

// different message types
type Message =
    | Start of string // parent starts spawning nodes. Nodes start joining
    | Register of User
    | Login of User
    | Logout of User
    | Tweet of Tweet
    | LiveTweet of Tweet
    | Query of Query
    | QueryResponse of Tweet array
    | Subscribe of Subscribe 
    // | StartRequestPhase // Nodes start making 1 request per second
    // | Join of string // route the Join packet
    // | JoinSuccess // parent know that a node has finished joining
    // | NewRow of int * string[] //  (row number, row of routing table)
    // | NewLeaves of Set<string> // leaf set
    // | Route of string * string * int // route the Route(request) packet
    // | RouteSuccess of int // report number of hops to parent
    // | RequestTick // tick every 1 second

// main program
let main numNodes =
    let system = ActorSystem.Create("ClientSimulator", configuration) // create an actor system

    let mutable usernames = Array.empty

    let getRandomInt start stop =  // get random integer [start, stop]
        let rnd = System.Random()
        rnd.Next(start, stop+1)
    
    let getRandomString n =
        let rnd = System.Random()
        let chars = Array.concat([[|'a' .. 'z'|];[|'A' .. 'Z'|];[|'0' .. '9'|]])
        let sz = Array.length chars in
        System.String(Array.init n (fun _ -> chars.[rnd.Next sz]))

    let getRandomTweet () =
        let getRandomMention () = 
            if (getRandomInt 0 1) = 0 then
                "@" + usernames.[(getRandomInt 0 (usernames.Length-1))]
            else
                ""
    
        let getRandomHashTag () = 
            if (getRandomInt 0 1) = 0 then
                "#" + getRandomString (getRandomInt 3 10)
            else
                ""
        let tweet = (getRandomString(getRandomInt 10 20)) + getRandomMention () + getRandomHashTag ()
        tweet

    let client (childMailbox: Actor<_>) = // client node
        let mId = childMailbox.Self.Path.Name // id
        // let mutable cancelable = Unchecked.defaultof<ICancelable> // to cancel making requests
        let mServer = system.ActorSelection("akka.tcp://twitter@127.0.0.1:9001/user/server")

        let mutable mUser = Unchecked.defaultof<User>

        let rec clientLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Register user -> // register is of type User
                    mUser <- user
                    mServer <! Register mUser
                | Subscribe subscribe ->
                    mServer <! Subscribe subscribe
                | Tweet tweet ->
                    mServer <! Tweet tweet
                | LiveTweet tweet ->
                    printfn "%s received live tweet %s" mId tweet.text
                | Query query ->
                    mServer <! Query query
                | QueryResponse response ->
                    printfn "Respone"
                    for tweet in response do
                        printfn "%A" tweet
                | _ -> return ()
                return! clientLoop()
            }
        clientLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let mutable mainSender = Unchecked.defaultof<IActorRef> // main program

                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Start start ->
                            printfn "parent received start"
                            for i in 1 .. numNodes do
                                let clientRef = spawn parentMailbox (string i) (client)
                                let username = getRandomString 5
                                let password = getRandomString 8
                                usernames <- Array.append usernames [|username|]
                                let user = {id=string i; username=username; password=password}
                                clientRef <! Register user

                                // Tweet
                                let tweet = {id="1"; reId=""; text="def #abc @ghi"; tType="tweet"; by=user} 
                                clientRef <! Tweet tweet
                                clientRef <! Tweet tweet

                                // Subscribe to self for testing
                                let subscribe = {publisher=username; subscriber=username}
                                clientRef <! Subscribe subscribe

                                // Query
                                let query = {qType="mention"; qName="@ghi"; by=user}
                                clientRef <! Query query      

                                // Retweet
                                let retweet = {id=getRandomString 5; reId="1"; text=""; tType="retweet"; by=user} 
                                clientRef <! Tweet retweet
                                // clientRef <! Tweet retweet                      
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
        let! response = parent <? Start "start"
        printfn ""
    } |> Async.RunSynchronously

let args : string array = fsi.CommandLineArgs |> Array.tail
let numNodes = int args.[0]
main numNodes