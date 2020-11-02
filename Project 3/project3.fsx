#r "nuget: Akka.FSharp"

open Akka.Actor
open Akka.FSharp
open System
open System.Diagnostics

// different message types
type Message =
    | Start of string // parent starts spawning nodes. Nodes start joining
    | StartRequestPhase // Nodes start making 1 request per second
    | Join of string // route the Join packet
    | JoinSuccess // parent know that a node has finished joining
    | NewRow of int * string[] //  (row number, row of routing table)
    | NewLeaves of Set<string> // leaf set
    | Route of string * string * int // route the Route(request) packet
    | RouteSuccess of int // report number of hops to parent
    | RequestTick // tick every 1 second

// main program
let main numNodes numRequests =
    use system = ActorSystem.Create("Project3") // create an actor system

    let getRandomInt start stop =  // get random integer [start, stop]
        let rnd = System.Random()
        rnd.Next(start, stop+1)

    let decimalTo (x: int) (b: int) = // convert an int to base b (b=16)
        System.Convert.ToString(x, b).PadLeft(8, '0')
    let toDecimal (x: string) = // from hexadecimal to decimal
        System.Convert.ToInt32(x, 16)    
    
    let hexToDecimalMap = Map.empty.Add('0', 0).Add('1', 1).Add('2', 2).Add('3', 3).Add('4', 4).Add('5', 5).Add('6', 6).Add('7', 7).Add('8', 8).Add('9', 9).Add('a', 10).Add('b', 11).Add('c', 12).Add('d', 13).Add('e', 14).Add('f', 15)
    let hexToDecimal (c:char) =
        hexToDecimalMap.[c]
    
    let longestCommonPrefix (s1:string) (s2:string) = // find longest common prefix
        let n1 = s1.Length
        let n2 = s2.Length
        let rec loop i j =
            if i<n1 && j<n2 then
                if s1.[i] = s2.[j] then loop (i+1) (j+1)
                else i-1
            else i-1
        let index = loop 0 0
        if index = -1 then ""
        else s1.[0..index]

    let findClosest id (set:Set<string>) = // find the node that is closest to id from set
        set
        |> Seq.mapi (fun i v -> v, abs(toDecimal(v)-toDecimal(id)))
        |> Seq.minBy snd
        |> fst

    let child (childMailbox: Actor<_>) = // pastry node
        let id = childMailbox.Self.Path.Name // id (hexadecimal)
        let mutable cancelable = Unchecked.defaultof<ICancelable> // to cancel making requests

        let routingTable = Array2D.create 16 16 "" // routing table (16x16)
        let mutable largerLeaves = Set.empty // large leaves
        let mutable smallerLeaves = Set.empty // small leaves

        let mutable nRequests = 0 // number of requests made by this node

        let updateSmallerLeaves newNode = // update smalle leaf set
            if smallerLeaves.IsEmpty || smallerLeaves.Count < 8 then
                smallerLeaves <- smallerLeaves.Add newNode
            elif smallerLeaves.Count = 8 then
                if newNode > smallerLeaves.MinimumElement then
                    smallerLeaves <- smallerLeaves.Remove smallerLeaves.MinimumElement
        
        let updateLargerLeaves newNode = // update large leaf set
            if largerLeaves.IsEmpty || largerLeaves.Count < 8 then
                largerLeaves <- largerLeaves.Add newNode
            elif largerLeaves.Count = 8 then
                if newNode < largerLeaves.MaximumElement then
                    largerLeaves <- largerLeaves.Remove largerLeaves.MaximumElement

        let sendLeaves destination = // send leaf set to destination
            let leaves = Set.union smallerLeaves largerLeaves
            system.ActorSelection("/user/parent/"+destination) <! NewLeaves (leaves)
        
        let getRowCol destination = //  find matching row and column
            let commonPrefix = longestCommonPrefix id destination
            let row = String.length commonPrefix
            if String.length(commonPrefix) < destination.Length then
                let col =  destination.[String.length(commonPrefix)] |> hexToDecimal
                row, col
            else
                row, 16

        let lookupRoutingTable destination = // find next hop node from routing table for a given destination
            let (row, col) = getRowCol destination
            routingTable.[row, col]

        let sendMatchingRows destination = // send a row from routing table
            let (row, _) = getRowCol destination
            for i in 0 .. row do
                system.ActorSelection("/user/parent/"+destination) <! NewRow (row, routingTable.[row, *].Clone() :?> string[])

        let forward source destination nHops = // 
            let nextNode = lookupRoutingTable destination
            if nextNode = "" then
                if destination < id && smallerLeaves.Count > 0 then // No entry in routing table. Using smaller leaves
                    system.ActorSelection("/user/parent/"+smallerLeaves.MinimumElement) <! Route (source, destination, nHops+1)
                elif destination > id && largerLeaves.Count > 0 then // No entry in routing table. Using larger leaves
                    system.ActorSelection("/user/parent/"+largerLeaves.MaximumElement) <! Route (source, destination, nHops+1)
                else // Consume the message
                    system.ActorSelection("/user/parent") <! RouteSuccess (nHops+1)
            else // next node found from the table
                system.ActorSelection("/user/parent/"+nextNode) <! Route (source, destination, nHops+1)

        let rec childLoop() =
            actor {   
                let! msg = childMailbox.Receive() // fetch the message from the queue
                let sender = childMailbox.Sender()
                match msg with
                | Start start ->
                    if start <> "" then
                        system.ActorSelection("/user/parent/"+start) <! Join id
                    else
                        system.ActorSelection("/user/parent") <! JoinSuccess
                | StartRequestPhase ->
                    // start making 1 request per second
                    cancelable <- system.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, (TimeSpan.FromMilliseconds(1000.0)), childMailbox.Self, (RequestTick), childMailbox.Self)
                | Join newNode ->
                    if id = newNode then // Join message reached the original sender
                        system.ActorSelection("/user/parent") <! JoinSuccess
                    else
                        if (lookupRoutingTable newNode) = "" then // if no entry then add entry using prefix match
                            let (row, col) = getRowCol newNode
                            routingTable.[row, col] <- newNode
                        if newNode < id then // add to small leaf set
                            updateSmallerLeaves newNode
                            system.ActorSelection("/user/parent/"+(findClosest id smallerLeaves)) <! Join newNode
                        elif newNode > id then  // add to large leaf set
                            updateLargerLeaves newNode
                            system.ActorSelection("/user/parent/"+(findClosest id largerLeaves)) <! Join newNode
                        sendMatchingRows newNode // send rows from routing table
                        sendLeaves newNode // send leaf set
                | NewRow (row_num, row) -> // add the new row to the routing table
                    for i in 0 .. row.Length-1 do
                        if routingTable.[row_num, i] = "" then
                            routingTable.[row_num, i] <- row.[i]
                | NewLeaves newLeaves -> //  add the nodes to the leaf set
                    for leaf in newLeaves do
                        if leaf <> id then
                            if leaf < id then updateSmallerLeaves leaf
                            else updateLargerLeaves leaf
                | RequestTick -> // make a request now
                    nRequests <- nRequests + 1
                    system.ActorSelection("/user/parent/"+id) <! Route (id, decimalTo (getRandomInt 1 numNodes) 16, -1)
                    if nRequests = numRequests then
                        cancelable.Cancel () // stop making request
                | Route (source, destination, nHops) ->
                    if id = destination then // consume message
                        system.ActorSelection("/user/parent") <! RouteSuccess (nHops+1)
                    else
                        if destination < id then
                            if smallerLeaves.Count > 0 then
                                if abs(toDecimal(id)-toDecimal(destination)) < abs(toDecimal(findClosest destination smallerLeaves)-toDecimal(destination)) then
                                    system.ActorSelection("/user/parent") <! RouteSuccess (nHops+1) // consume if I am the destination is closest to me (of all the nodes in small leafset)
                                elif destination >= smallerLeaves.MinimumElement then // if destination is in the range of small leafset
                                    system.ActorSelection("/user/parent/"+(findClosest id smallerLeaves)) <! Route (source, destination, nHops+1)
                                else // use routing table
                                    forward source destination nHops 
                            else // use routing table
                                forward source destination nHops
                        if destination > id then
                            if largerLeaves.Count > 0 then
                                if abs(toDecimal(id)-toDecimal(destination)) < abs(toDecimal(findClosest destination largerLeaves)-toDecimal(destination)) then
                                    system.ActorSelection("/user/parent") <! RouteSuccess (nHops+1) // consume if I am the destination is closest to me (of all the nodes in large leafset)
                                elif destination <= largerLeaves.MaximumElement then // if destination is in the range of small leafset
                                    system.ActorSelection("/user/parent/"+(findClosest id largerLeaves)) <! Route (source, destination, nHops+1)
                                else // use routing table
                                    forward source destination nHops
                            else // use routing table
                                forward source destination nHops
                | _ -> return ()
                return! childLoop()
            }

        childLoop()

    let parent = // job assignment actor (parent, supervisor)
        spawnOpt system "parent"
            <| fun parentMailbox ->
                let mutable mainSender = Unchecked.defaultof<IActorRef> // main program

                let activeNodes = Array.create numNodes "" // nodes currently alive in the network
                let mutable nSuccessfulJoins = 0 // number of nodes that have joined
                let mutable nSuccessfulRequests = 0 // number of requests served so far
                let mutable nHops = 0 // total number of hop so far
                
                let maxId = 10000000 // max node id
                let set = [| for i in 1 .. maxId -> i |]
                let mutable lastIndex = maxId-1
                let getUniqueRandomNumber () = // get unique id for nodes
                    let idx = getRandomInt 0 lastIndex
                    let picked = set.[idx]
                    set.[idx] <- set.[lastIndex]
                    set.[lastIndex] <- picked
                    lastIndex <- lastIndex - 1
                    picked

                let rec parentLoop() =
                    actor {
                        let! (msg: Message) = parentMailbox.Receive() // fetch the message from the queue
                        let sender = parentMailbox.Sender()
                        match msg with
                        | Start start -> // spawn pastry nodes one by one
                            printfn "Join phase: Please wait for all the nodes to join the network"
                            mainSender <- sender
                            for i in 1 .. numNodes do
                                let id = decimalTo (getUniqueRandomNumber ()) 16
                                activeNodes.[i-1] <- id
                                let childRef = spawn parentMailbox id child
                                if i = 1 then
                                    childRef <! Start ""
                                else
                                    let poc = activeNodes.[getRandomInt 0 (i-2)] 
                                    childRef <! Start poc
                        | JoinSuccess -> // a node has joined successfully
                            nSuccessfulJoins <- nSuccessfulJoins + 1
                            if nSuccessfulJoins = numNodes then
                                printfn "All nodes have joined and routing tables have converged"
                                printfn "Request phase: Nodes will now make 1 request per second"
                                system.ActorSelection("/user/parent/*") <! StartRequestPhase
                        | RouteSuccess hops-> // a request has been served
                            nSuccessfulRequests <- nSuccessfulRequests + 1
                            nHops <- nHops + hops
                            if nSuccessfulRequests = (numRequests * numNodes) then
                                printfn "All requests have been served"
                                printfn "Average hops per requests = %f" (float(nHops)/(float(numRequests)*float(numNodes)))
                                system.Terminate() |> ignore
                                mainSender <! "Done"
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
        let! response = parent <? Start "0"
        printfn ""
    } |> Async.RunSynchronously

let args : string array = fsi.CommandLineArgs |> Array.tail
let numNodes = int args.[0]
let numRequests = int args.[1]
main numNodes numRequests