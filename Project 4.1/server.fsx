#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#r "nuget: Akka.Serialization.Hyperion"

open Akka.Actor
open Akka.FSharp
open Akka.Configuration
open Akka.Serialization
open System.Diagnostics

open System
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
let main () =
    let system = ActorSystem.Create("twitter", configuration) // create an actor system

    let statsInterval = 5.0 // time interval in seconds to print stats

    let regexHashTag = "#[A-Za-z0-9]+" // regex for hashtag
    let regexMention = "@[A-Za-z0-9]+" // regex for mention

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
                let clientSimulatorAddress = "akka.tcp://ClientSimulator@127.0.0.1:9002/user/parent/" // clients address
                
                // in memory Database
                let mutable onlineUsers: Set<string> = Set.empty // set of usernames of online users
                let mutable usersMap: Map<string, User> = Map.empty // username -> User
                let mutable subscriptionsMap: Map<string, Set<string>> = Map.empty // subscriptions of a given user
                let mutable subscribersMap: Map<string, string array> = Map.empty // subscribers of a given user
                let mutable tweetsMap: Map<string, Tweet> = Map.empty // tweetId -> Tweet
                let mutable tweetsByUsername: Map<string, string array> = Map.empty // username -> tweetId
                let mutable tweetsByHashTag: Map<string, string array> = Map.empty // hashtag -> tweetId
                let mutable tweetsByMention: Map<string, string array> = Map.empty // mention -> tweetId

                // Stats
                let mutable lastTweetCount = 0.0 // previous tweet count
                let mutable currentTweetCount = 0.0 // current tweet count
                let mutable totalUsers = 0.0 // total no of users

                let addTweet tweet = // add this tweet to the database
                    tweetsMap <- tweetsMap.Add(tweet.id, tweet) // add to tweets Map
                    let mutable tweetIds = if tweetsByUsername.ContainsKey(tweet.by.username) then tweetsByUsername.Item(tweet.by.username) else [||]
                    tweetIds <- Array.append tweetIds [|tweet.id|]
                    tweetsByUsername <- tweetsByUsername.Add(tweet.by.username, tweetIds) // add to tweetsByUsername

                    let hashTags = getPatternMatches regexHashTag tweet.text
                    let mentions = getPatternMatches regexMention tweet.text
                    
                    // add to tweetsByHashTag
                    for tag in hashTags do
                        let mutable tweetIds = if tweetsByHashTag.ContainsKey(tag) then tweetsByHashTag.Item(tag) else [||]
                        tweetIds <- Array.append tweetIds [|tweet.id|]
                        tweetsByHashTag <- tweetsByHashTag.Add(tag, tweetIds)
                    
                    // add to tweetsByMention
                    for mention in mentions do
                        let mutable tweetIds = if tweetsByMention.ContainsKey(mention) then tweetsByMention.Item(mention) else [||]
                        tweetIds <- Array.append tweetIds [|tweet.id|]
                        tweetsByMention <- tweetsByMention.Add(mention, tweetIds)
                
                let pushTweet (tweet: Tweet) = // push this live tweet to online users if they have subscribed
                    // Send tweets to online users
                    if subscribersMap.ContainsKey(tweet.by.username) then
                        for subscriber in subscribersMap.Item(tweet.by.username) do
                            if onlineUsers.Contains(subscriber) then
                                system.ActorSelection(clientSimulatorAddress+usersMap.Item(subscriber).id) <! LiveTweet tweet
                                // printfn "Tweet sent to online user %s" subscriber
                
                let rec loop() =
                    actor {   
                        let! (msg: Message) = mailbox.Receive() // fetch the message from the queue
                        let sender = mailbox.Sender()
                        match msg with
                        | Register user ->
                            totalUsers <- totalUsers + 1.0
                            usersMap <- usersMap.Add(user.username, user)
                            onlineUsers <- onlineUsers.Add(user.username)
                            // printfn "User %s registered" user.username
                        | Login user ->
                            onlineUsers <- onlineUsers.Add(user.username)
                            // printfn "User %s logged in" user.username
                        | Logout user ->
                            onlineUsers <- onlineUsers.Remove(user.username)
                            // printfn "User %s logged out" user.username
                        | Tweet tweet ->
                            currentTweetCount <- currentTweetCount + 1.0
                            if tweet.tType = "tweet" then // if new tweet
                                // printfn "New Tweet [%s] by [%s]" tweet.text tweet.by.username
                                addTweet tweet
                                pushTweet tweet
                            else // if it is a retweet
                                if tweetsMap.ContainsKey(tweet.reId) then
                                    let retweet = {tweet with text=tweetsMap.Item(tweet.reId).text}
                                    // printfn "ReTweet [%s] by [%s]" tweet.text tweet.by.username
                                    addTweet retweet
                                    pushTweet retweet
                            
                        | Subscribe subscribe -> // subscribe to a publisher
                            if not (subscriptionsMap.ContainsKey(subscribe.subscriber)) then    
                                let mutable subscriptions = if subscriptionsMap.ContainsKey(subscribe.subscriber) then Set.toArray(subscriptionsMap.Item(subscribe.subscriber)) else [||]
                                subscriptions <- Array.append subscriptions [|subscribe.publisher|]
                                subscriptions <- Array.append subscriptions [|subscribe.publisher|]
                                subscriptionsMap <- subscriptionsMap.Add(subscribe.subscriber, Set.ofArray subscriptions)

                                let mutable subscribers = if subscribersMap.ContainsKey(subscribe.publisher) then subscribersMap.Item(subscribe.publisher) else [||]
                                subscribers <- Array.append subscribers [|subscribe.subscriber|]
                                subscribers <- Array.append subscribers [|subscribe.subscriber|]
                                subscribersMap <- subscribersMap.Add(subscribe.publisher, subscribers)
                                // printfn "%s subscribed to %s" subscribe.subscriber subscribe.publisher
                        | Query query -> // query
                            printfn "Query from %s" query.by.username
                            let mutable response: Tweet array = Array.empty // response
                            match query.qType with
                            | "subscription" -> // my subscriptions
                                let subscriptions = if subscriptionsMap.ContainsKey(query.by.username) then Set.toArray(subscriptionsMap.Item(query.by.username)) else [||]
                                for publisher in subscriptions do
                                    let tweetIds = if tweetsByUsername.ContainsKey(publisher) then tweetsByUsername.Item(publisher) else [||]
                                    for tweetId in tweetIds do
                                        response <- Array.append response [|tweetsMap.Item(tweetId)|]
                            | "hashtag" -> // tweets of a given hashtag
                                let tweetIds = if tweetsByHashTag.ContainsKey(query.qName) then tweetsByHashTag.Item(query.qName) else [||]
                                for tweetId in tweetIds do
                                    response <- Array.append response [|tweetsMap.Item(tweetId)|]
                            | "mention" -> // tweets containing a user's username
                                let tweetIds = if tweetsByMention.ContainsKey(query.qName) then tweetsByMention.Item(query.qName) else [||]
                                for tweetId in tweetIds do
                                    response <- Array.append response [|tweetsMap.Item(tweetId)|]
                            | _ -> printfn "Invalid Query Type"
                            sender <! QueryResponse response
                        | PrintStats -> // Print Stats
                            printfn "----------STATS----------"
                            printfn "Total Tweets: %f" currentTweetCount
                            printfn "Tweets per second: %f" ((currentTweetCount-lastTweetCount)/statsInterval)
                            printfn "Total Users: %f" totalUsers
                            printfn "Online Users: %d" onlineUsers.Count
                            lastTweetCount <- currentTweetCount
                        | _ -> return ()
                        return! loop()
                    }
                loop()
    // print stats every statsInterval seconds
    system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, TimeSpan.FromSeconds(statsInterval), server, PrintStats, server) |> ignore

    async {
        System.Console.ReadLine() |> ignore
        printfn ""
    } |> Async.RunSynchronously


main ()