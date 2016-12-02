﻿module Analysis

type SqlType =
    | Int
    | Varchar
    | NVarchar
    | Unknown

type Columns = (string * SqlType) list

type Id =
    | Named of string
    | Unnamed
    | Star

type Projected = (string * SqlType) list

type SqlExpr =
    | IdExpr of Id
    | AliasExpr of SqlExpr * string
    | CastExpr of SqlExpr * SqlType
    | CountExpr of SqlExpr

type SelectStmt = { Projection: SqlExpr list; Source: Columns }

let tuple2 x y = (x, y)
let mapFst f (x, y) = (f x, y)

// TODO: qualified names

let single xs =
    match xs with
    | [x] -> x
    | _ -> failwith "Must be single item"

let analyze (stmt: SelectStmt) : Projected =
    let rec analyzeExpr expr =
        match expr with
        | IdExpr(Named id) -> [List.find (fst >> (=) id) stmt.Source]
        | IdExpr(Star) -> stmt.Source
        | IdExpr(Unnamed) -> failwith "Shouldn't have Unnamed here"
        | AliasExpr(body, id) -> [id, analyzeExpr body |> single |> snd]
        | CastExpr(body, typ) -> [analyzeExpr body |> single |> fst, typ]
        | CountExpr _ -> ["", Int]
    List.map analyzeExpr stmt.Projection |> List.concat
