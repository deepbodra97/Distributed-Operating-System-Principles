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
    qName: string // "" for subscription | "#<hashtag>" for hashtag | "@<username>" for mention
    by: User
}

type Subscribe = {
    publisher: string // publishers username
    subscriber: string // subscribers username
}

// different message types
type Message =
    | Start of string // parent starts spawning clients
    | Register of User // Client Register
    | Login of User // client logins / connects
    | Logout of User // client logouts / disconnects
    | UserBehavior of string // publisher | reader | lazy
    | Tweet of Tweet // to tweet
    | LiveTweet of Tweet // new tweet when the user is online
    | Query of Query // query for tweets
    | QueryResponse of Tweet array // response to the Query
    | Subscribe of Subscribe // to subscribe to a user
    | Tick // take one of the [login, logout, tweet, retweet, query] actions
    | PrintStats // not used for client

// main program
let main numNodes =
    let system = ActorSystem.Create("ClientSimulator", configuration) // create an actor system

    // User Behaviour Parameters
    let behaviorPercentPublisher = [|5; 15; 80|] // [disconnect, query, tweet] for aggressive publisher 
    let tickRangePublisher = [|5000; 10000|] // take an action every 5-10 seconds

    let behaviorPercentReader = [|5; 80; 15|] // for aggressive reader
    let tickRangeReader = [|5000; 10000|] // take an action every 5-10 seconds

    let behaviorPercentLazy = [|30; 35; 35|]
    let tickRangeLazy = [|10000; 15000|] // take an action every 10-15 seconds

    let maxLenTweetId = 3 // max string length for a tweet's id

    let mutable zipfConstant = 0.0 // summation of 1/i  for i in range [1,...numNodes]

    let queryTypes = [|"subscription"; "hashtag"; "mention"|] // different query types

    let mutable usernames = Array.empty // all usernames

    let getRandomInt start stop =  // get random integer [start, stop]
        let rnd = System.Random()
        rnd.Next(start, stop+1)
    
    let getRandomString n = // generate random alphnumeric string of length n
        let rnd = System.Random()
        let chars = Array.concat([[|'a' .. 'z'|];[|'A' .. 'Z'|];[|'0' .. '9'|]])
        let sz = Array.length chars in
        System.String(Array.init n (fun _ -> chars.[rnd.Next sz]))

    let getRandomHashTag () = // generate random hashtag
            if (getRandomInt 0 1) = 0 then
                "#" + getRandomString (getRandomInt 3 4)
            else
                ""
    
    let getRandomMention () = // generate random hashtag
            if (getRandomInt 0 1) = 0 then
                "@" + usernames.[(getRandomInt 0 (usernames.Length-1))]
            else
                ""

    let getRandomTweet () = // generate random tweet
        let tweet = (getRandomString(getRandomInt 10 20)) + " " + getRandomMention ()+ " " + getRandomHashTag ()
        tweet

    let getRandomQuery user () = // generate random query
        let qType = queryTypes.[(getRandomInt 0 (queryTypes.Length-1))]
        match qType with
        | "subscription" -> Query{qType=qType; qName=""; by=user}
        | "hashtag" -> Query{qType=qType; qName=(getRandomHashTag ()); by=user}
        | "mention" -> Query{qType=qType; qName=(getRandomMention ()); by=user}

    let client (childMailbox: Actor<_>) = // client node
        let mId = childMailbox.Self.Path.Name // id of client [1,..numNodes]
        let mServer = system.ActorSelection("akka.tcp://twitter@127.0.0.1:9001/user/server") // reference to server

        let mutable mUser = Unchecked.defaultof<User>
        let mutable mBehavior = ""
        let mutable mActive = true // to simulate connection/disconnection with/from the servers
        let mSelf = system.ActorSelection("/user/parent/"+mId) // reference to self to send Tick

        let mLogin () = // send login tick to self
            printfn "User %s is logging in" mUser.username
            mActive <- true
            mSelf <! Login mUser

        let mLogout () = // send logout tick to self
            printfn "User %s is logging out" mUser.username
            mActive <- false
            mSelf <! Logout mUser

        let mQuery () = // send query tick to self
            printfn "User %s is querying" mUser.username
            mSelf <! getRandomQuery mUser

        let mTweet () = // send tweet tick to self
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
                | Register user -> // register with the server
                    mUser <- user
                    mServer <! Register mUser
                | Login user -> // login into the server
                    mServer <! Login user
                | Logout user -> // logout from the server
                    mServer <! Logout user
                | UserBehavior behavior -> // set simulation behavior of this user
                    mBehavior <- behavior
                    printfn "User %s is %s" mUser.username mBehavior
                | Subscribe s ->
                    let numToSubscribe = int(zipfConstant*float(numNodes)/float(mUser.id)) // find number of users to subscribe to using zipf distribution
                    for i in 1 .. numToSubscribe do // subscribe to the users
                        let publisher = usernames.[(getRandomInt 0 (usernames.Length-1))]
                        // printfn "User %s is subscribing to %s" mUser.username publisher
                        let subscribe = {s with publisher=publisher; subscriber=mUser.username}
                        mServer <! Subscribe subscribe
                | Tweet tweet -> // post a tweet
                    mServer <! Tweet tweet
                | LiveTweet tweet -> // got a live tweet from the server
                    printfn "User %s received live tweet: %s" mUser.id tweet.text
                | Query query -> // query the server for tweets
                    mServer <! Query query
                | QueryResponse response -> // response from the server for a query
                    for tweet in response do
                        printfn "User %s received response: %s" mUser.username tweet.text
                | Tick -> // decide what action to take based on the random number and user behavior
                    let rnd = getRandomInt 1 100
                    match mBehavior with
                    | "publisher" -> // aggressive publisher behavior
                        if mActive then
                            if rnd <= behaviorPercentPublisher.[0] then // logout with p=0.05 (p=probability)
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
                    | "reader" -> // aggressive reader behavior
                        if mActive then
                            if rnd <= behaviorPercentReader.[0] then // logout with p=0.05
                                mLogout ()
                            elif rnd <= (behaviorPercentReader.[1]-behaviorPercentReader.[0]) then // query with p=0.80
                                mQuery ()
                            else // post with p=0.2
                                mTweet ()  
                        else // if logged out
                            if rnd <= (100-behaviorPercentReader.[0]) then // login with p=0.95
                                mLogin ()
                        let nextTickTime = getRandomInt tickRangeReader.[0] tickRangeReader.[1] |> float
                        system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(nextTickTime)), childMailbox.Self, (Tick), childMailbox.Self) |> ignore
                    | "lazy" -> // lazy behavior
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

    let parent = // parent
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let mutable mainSender = Unchecked.defaultof<IActorRef> // main program

                let lazyPercent = 80 // 80% of the users are lazy
                let agressivePublisherPercent = 10 // 10% of the users are aggressive publishers
                let agressiveReaderPercent = 10 // 10% of the users are aggressive readers

                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Start start ->
                            for i in 1 .. numNodes do
                                zipfConstant <- zipfConstant + 1.0/float(i) // zipf constant calculation

                                let clientRef = spawn parentMailbox (string i) (client) // new client
                                let username = getRandomString 5
                                let password = getRandomString 8
                                usernames <- Array.append usernames [|username|]
                                let user = {id=string i; username=username; password=password}
                                clientRef <! Register user // ask this client to register with the server

                                // set user behavior
                                if i <= lazyPercent*numNodes/100 then
                                    clientRef <! UserBehavior "lazy"
                                elif i <= (lazyPercent+agressivePublisherPercent)*numNodes/100 then
                                    clientRef <! UserBehavior "publisher"
                                else
                                    clientRef <! UserBehavior "reader"
                            
                            // Ask the users to subscribe                            
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