#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"

open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open Akka.Serialization
open System.Diagnostics

open System.Text.RegularExpressions

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
                hostname = 127.0.0.1
                port = 9001
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
    | Tweet of Tweet
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
let main () =
    let system = ActorSystem.Create("twitter", configuration) // create an actor system

    let regexHashTag = "#[A-Za-z0-9]+"
    let regexMention = "@[A-Za-z0-9]+"

    let (|Regex|_|) pattern input =
        let matches = Regex.Matches(input, pattern)
        Some([ for m in matches -> m.Value])
    
    let getPatternMatches regex tweet =
        match tweet with
        | Regex regex tags -> tags
        | _ -> []

    let server =
        spawn system "server"
            <| fun mailbox ->
                let id = mailbox.Self.Path.Name // id

                let mutable usersMap: Map<string, User> = Map.empty
                let mutable subscriptionsMap: Map<string, string array> = Map.empty // subscriptions of a given user
                let mutable subscribersMap: Map<string, string array> = Map.empty // subscribers of a given user
                let mutable tweetsMap: Map<string, Tweet> = Map.empty
                let mutable tweetsByUsername: Map<string, string array> = Map.empty
                let mutable tweetsByHashTag: Map<string, string array> = Map.empty
                let mutable tweetsByMention: Map<string, string array> = Map.empty

                let rec loop() =
                    actor {   
                        let! (msg: Message) = mailbox.Receive() // fetch the message from the queue
                        let sender = mailbox.Sender()
                        match msg with
                        | Register user ->
                            usersMap <- usersMap.Add(user.username, user)
                            printfn "User %A registered" user
                        | Tweet tweet ->
                            let newTweet = if tweet.tType = "tweet" then tweet else {tweet with text=tweetsMap.Item(tweet.reId).text}
                            tweetsMap <- tweetsMap.Add(newTweet.id, newTweet)

                            let mutable tweetIds = if tweetsByUsername.ContainsKey(tweet.by.username) then tweetsByUsername.Item(newTweet.by.username) else [||]
                            tweetIds <- Array.append tweetIds [|newTweet.id|]
                            tweetsByUsername <- tweetsByUsername.Add(newTweet.by.username, tweetIds)

                            let hashTags = getPatternMatches regexHashTag newTweet.text
                            let mentions = getPatternMatches regexMention newTweet.text
                            
                            for tag in hashTags do
                                let mutable tweetIds = if tweetsByHashTag.ContainsKey(tag) then tweetsByHashTag.Item(tag) else [||]
                                tweetIds <- Array.append tweetIds [|newTweet.id|]
                                tweetsByHashTag <- tweetsByHashTag.Add(tag, tweetIds)
                            
                            for mention in mentions do
                                let mutable tweetIds = if tweetsByMention.ContainsKey(mention) then tweetsByMention.Item(mention) else [||]
                                tweetIds <- Array.append tweetIds [|newTweet.id|]
                                tweetsByMention <- tweetsByMention.Add(mention, tweetIds)
                            
                            printfn "%A %A %A" tweetsByUsername tweetsByHashTag tweetsByMention
                            printfn "New Tweet %A" newTweet
                        | Subscribe subscribe ->
                            let mutable subscriptions = if subscriptionsMap.ContainsKey(subscribe.subscriber) then subscriptionsMap.Item(subscribe.subscriber) else [||]
                            subscriptions <- Array.append subscriptions [|subscribe.publisher|]
                            subscriptions <- Array.append subscriptions [|subscribe.publisher|]
                            subscriptionsMap <- subscriptionsMap.Add(subscribe.subscriber, subscriptions)

                            let mutable subscribers = if subscribersMap.ContainsKey(subscribe.publisher) then subscribersMap.Item(subscribe.publisher) else [||]
                            subscribers <- Array.append subscribers [|subscribe.subscriber|]
                            subscribers <- Array.append subscribers [|subscribe.subscriber|]
                            subscribersMap <- subscribersMap.Add(subscribe.publisher, subscribers)
                            printfn "%s subscribed to %s" subscribe.subscriber subscribe.publisher
                        | Query query ->
                            let mutable response: Tweet array = Array.empty
                            match query.qType with
                            | "subscription" ->
                                let subscriptions = if subscriptionsMap.ContainsKey(query.by.username) then subscriptionsMap.Item(query.by.username) else [||]
                                for publisher in subscriptions do
                                    let tweetIds = if tweetsByUsername.ContainsKey(publisher) then tweetsByUsername.Item(publisher) else [||]
                                    for tweetId in tweetIds do
                                        response <- Array.append response [|tweetsMap.Item(tweetId)|]
                            | "hashtag" ->
                                for tweetId in tweetsByHashTag.Item(query.qName) do
                                    response <- Array.append response [|tweetsMap.Item(tweetId)|]
                            | "mention" ->
                                for tweetId in tweetsByMention.Item(query.qName) do
                                    response <- Array.append response [|tweetsMap.Item(tweetId)|]
                            | _ -> printfn "Invalid Query Type"
                            sender <! QueryResponse response
                        | _ -> return ()
                        return! loop()
                    }
                loop()


    async {
        // let! response = server <? ()
        System.Console.ReadLine() |> ignore
        printfn ""
    } |> Async.RunSynchronously


main ()