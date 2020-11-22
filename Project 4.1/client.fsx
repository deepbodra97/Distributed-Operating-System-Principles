#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"
open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open Akka.Serialization

open System

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
    qType: string // subscription | hashtag | mention
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
    | UserBehavior of string
    | Tweet of Tweet
    | LiveTweet of Tweet
    | Query of Query
    | QueryResponse of Tweet array
    | Subscribe of Subscribe
    | Tick
    | PrintStats // not used for client

// main program
let main numNodes =
    let system = ActorSystem.Create("ClientSimulator", configuration) // create an actor system

    // User Behaviour Parameters
    let behaviorPercentPublisher = [|5; 15; 80|] // [disconnect, query, tweet]
    let tickRangePublisher = [|5000; 10000|]

    let behaviorPercentReader = [|5; 80; 15|]
    let tickRangeReader = [|5000; 10000|]

    let behaviorPercentLazy = [|30; 35; 35|]
    let tickRangeLazy = [|10000; 15000|]

    let maxLenTweetId = 3

    let mutable usernames = Array.empty
    let mutable zipfConstant = 0.0

    let queryTypes = [|"subscription"; "hashtag"; "mention"|]

    let getRandomInt start stop =  // get random integer [start, stop]
        let rnd = System.Random()
        rnd.Next(start, stop+1)
    
    let getRandomString n =
        let rnd = System.Random()
        let chars = Array.concat([[|'a' .. 'z'|];[|'A' .. 'Z'|];[|'0' .. '9'|]])
        let sz = Array.length chars in
        System.String(Array.init n (fun _ -> chars.[rnd.Next sz]))

    let getRandomHashTag () = 
            if (getRandomInt 0 1) = 0 then
                "#" + getRandomString (getRandomInt 3 4)
            else
                ""
    
    let getRandomMention () = 
            if (getRandomInt 0 1) = 0 then
                "@" + usernames.[(getRandomInt 0 (usernames.Length-1))]
            else
                ""

    let getRandomTweet () =
        let tweet = (getRandomString(getRandomInt 10 20)) + " " + getRandomMention ()+ " " + getRandomHashTag ()
        tweet

    let getRandomQuery user () =
        let qType = queryTypes.[(getRandomInt 0 (queryTypes.Length-1))]
        match qType with
        | "subscription" -> Query{qType=qType; qName=""; by=user}
        | "hashtag" -> Query{qType=qType; qName=(getRandomHashTag ()); by=user}
        | "mention" -> Query{qType=qType; qName=(getRandomMention ()); by=user}

    let client (childMailbox: Actor<_>) = // client node
        let mId = childMailbox.Self.Path.Name // id
        // let mutable cancelable = Unchecked.defaultof<ICancelable> // to cancel making requests
        let mServer = system.ActorSelection("akka.tcp://twitter@127.0.0.1:9001/user/server")

        let mutable mUser = Unchecked.defaultof<User>
        let mutable mBehavior = ""
        let mutable mActive = true
        let mSelf = system.ActorSelection("/user/parent/"+mId)

        let mLogin () =
            printfn "User %s is logging in" mUser.username
            mActive <- true
            mSelf <! Login mUser

        let mLogout () =
            printfn "User %s is logging out" mUser.username
            mActive <- false
            mSelf <! Logout mUser

        let mQuery () =
            printfn "User %s is querying" mUser.username
            mSelf <! getRandomQuery mUser

        let mTweet () =
            if (getRandomInt 0 1) = 0 then
                let tweetText = getRandomTweet ()
                printfn "User %s is tweeting: %s" mUser.username tweetText
                mSelf <! Tweet {id=getRandomString maxLenTweetId; reId=""; text=tweetText; tType="tweet"; by=mUser} 
            else
                printfn "User %s is retweeting" mUser.username
                mSelf <! Tweet {id=getRandomString maxLenTweetId; reId=getRandomString maxLenTweetId; text=""; tType="retweet"; by=mUser}


        let rec clientLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Register user -> // register is of type User
                    mUser <- user
                    mServer <! Register mUser
                | Login user ->
                    mServer <! Login user
                | Logout user ->
                    mServer <! Logout user
                | UserBehavior behavior ->
                    mBehavior <- behavior
                    printfn "User %s is %s" mUser.username mBehavior
                | Subscribe s ->
                    let numToSubscribe = int(zipfConstant*float(numNodes)/float(mUser.id))
                    for i in 1 .. numToSubscribe do
                        let publisher = usernames.[(getRandomInt 0 (usernames.Length-1))]
                        // printfn "User %s is subscribing to %s" mUser.username publisher
                        let subscribe = {s with publisher=publisher; subscriber=mUser.username}
                        mServer <! Subscribe subscribe
                | Tweet tweet ->
                    mServer <! Tweet tweet
                | LiveTweet tweet ->
                    printfn "User %s received live tweet: %s" mUser.id tweet.text
                | Query query ->
                    mServer <! Query query
                | QueryResponse response ->
                    for tweet in response do
                        printfn "User %s received response: %s" mUser.username tweet.text
                | Tick ->
                    let rnd = getRandomInt 1 100
                    match mBehavior with
                    | "publisher" ->
                        let self = system.ActorSelection("/user/parent/"+mId)
                        if mActive then
                            if rnd <= behaviorPercentPublisher.[0] then // logout with p=0.05
                                mLogout ()
                            elif rnd <= (behaviorPercentPublisher.[1]-behaviorPercentPublisher.[0]) then // query with p=0.15
                                mQuery ()
                            else // post with p=0.8
                                mTweet ()  
                        else // if logged out
                            if rnd <= (100-behaviorPercentPublisher.[0]) then // login with p=0.95
                                mLogin ()
                        let nextTickTime = getRandomInt tickRangePublisher.[0] tickRangePublisher.[1] |> float
                        system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(nextTickTime)), childMailbox.Self, (Tick), childMailbox.Self) |> ignore
                    | "reader" ->
                        let self = system.ActorSelection("/user/parent/"+mId)
                        if mActive then
                            if rnd <= behaviorPercentReader.[0] then // logout with p=0.05
                                mLogout ()
                            elif rnd <= (behaviorPercentReader.[1]-behaviorPercentReader.[0]) then // query with p=0.80
                                mQuery ()
                            else // post with p=0.8
                                mTweet ()  
                        else // if logged out
                            if rnd <= (100-behaviorPercentReader.[0]) then // login with p=0.15
                                mLogin ()
                        let nextTickTime = getRandomInt tickRangeReader.[0] tickRangeReader.[1] |> float
                        system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(nextTickTime)), childMailbox.Self, (Tick), childMailbox.Self) |> ignore
                    | "lazy" ->
                        let self = system.ActorSelection("/user/parent/"+mId)
                        if mActive then
                            if rnd <= behaviorPercentLazy.[0] then // logout with p=0.30
                                mLogout ()
                            elif rnd <= (behaviorPercentLazy.[1]-behaviorPercentLazy.[0]) then // query with p=0.35
                                mQuery ()
                            else // post with p=0.35
                                mTweet ()  
                        else // if logged out
                            if rnd <= (100-behaviorPercentLazy.[0]) then // login with p=0.70
                                mLogin ()
                        let nextTickTime = getRandomInt tickRangeLazy.[0] tickRangeLazy.[1] |> float
                        system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(nextTickTime)), childMailbox.Self, (Tick), childMailbox.Self) |> ignore
                    | _ -> return ()
                    mSelf <! Tick

                | _ -> return ()
                return! clientLoop()
            }
        clientLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let mutable mainSender = Unchecked.defaultof<IActorRef> // main program

                let lazyPercent = 80
                let agressivePublisherPercent = 10
                let agressiveReaderPercent = 10

                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Start start ->
                            for i in 1 .. numNodes do
                                zipfConstant <- zipfConstant + 1.0/float(i)

                                let clientRef = spawn parentMailbox (string i) (client)
                                let username = getRandomString 5
                                let password = getRandomString 8
                                usernames <- Array.append usernames [|username|]
                                let user = {id=string i; username=username; password=password}
                                clientRef <! Register user

                                if i <= lazyPercent*numNodes/100 then
                                    clientRef <! UserBehavior "lazy"
                                elif i <= (lazyPercent+agressivePublisherPercent)*numNodes/100 then
                                    clientRef <! UserBehavior "publisher"
                                else
                                    clientRef <! UserBehavior "reader"
                            
                            // Subscribe                            
                            let subscribe = {publisher=""; subscriber=""}
                            system.ActorSelection("/user/parent/*") <! Subscribe subscribe
                            system.ActorSelection("/user/parent/*") <! Tick              
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