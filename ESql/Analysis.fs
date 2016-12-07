﻿module Analysis

type SqlType =
    | Bit
    | Int
    | Varchar
    | NVarchar
    | Unknown

type Op =
    | Eq
    | Gt
    | Lt
    | And
    | Or

type Columns = (string * SqlType) list

type Id =
    | Qualified of string * Id
    | Named of string
    | Param of string
    | Unnamed
    | Star

type Projection = (string option * SqlType) list

type SqlExpr =
    | ConstExpr of SqlType
    | IdExpr of Id
    | AliasExpr of SqlExpr * string
    | CastExpr of SqlExpr * SqlType
    | CountExpr of SqlExpr
    | BinaryExpr of Op * SqlExpr * SqlExpr

type Sources = (string * Columns) list

type SelectStmt = { Selections: SqlExpr list; Sources: Sources }

let mapFst f (x, y) = (f x, y)

let single xs =
    match xs with
    | [x] -> x
    | _ -> failwith "Must be single item"

let inferProjection (stmt: SelectStmt) : Projection =
    let analyzeIdForTable id (cols: Columns) =
        match id with
        | Qualified _ -> failwith "Shouldn't have Qualified here"
        | Named name -> [cols |> List.find (fst >> (=) name) |> mapFst Some]
        | Star -> List.map (mapFst Some) cols
        | Unnamed -> failwith "Shouldn't have Unnamed here"
        | Param _ -> failwith "Shouldn't have Param here"
    let analyzeId (id: Id) (sources: Sources) =
        match id with
        | Qualified(qualifier, id) -> sources |> List.find (fst >> (=) qualifier) |> snd |> analyzeIdForTable id
        | Named name -> [sources |> List.map snd |> List.concat |> List.filter (fst >> (=) name) |> single |> mapFst Some]
        | Star -> sources |> List.map snd |> List.concat |> List.map (mapFst Some)
        | Unnamed -> failwith "Shouldn't have Unnamed here"
        | Param _ -> failwith "Shouldn't have Param here"
    let rec analyzeExpr expr =
        match expr with
        | ConstExpr typ -> [None, typ]
        | IdExpr id -> analyzeId id stmt.Sources
        | AliasExpr(body, id) -> [Some id, analyzeExpr body |> single |> snd]
        | CastExpr(body, typ) -> [analyzeExpr body |> single |> fst, typ]
        | CountExpr _ -> [None, Int]
        | BinaryExpr(_, left, right) -> [None, Bit]
    stmt.Selections |> List.map analyzeExpr |> List.concat

type WhereClause = { Condition: SqlExpr; Sources: Sources }

type Parameters = (string * SqlType) list

let inferType expr =
    match expr with
    | ConstExpr typ -> typ
    | CastExpr(_, typ) -> typ
    | _ -> failwith "Can't infer type"

let inferParameters (clause: WhereClause) : Parameters =
    let rec analyzeExpr expr =
        match expr with
        | BinaryExpr(_, IdExpr(Param name), expr) -> [name, inferType expr]
        | BinaryExpr(_, expr, IdExpr(Param name)) -> [name, inferType expr]
        | _ -> failwith "Can't analyze expression"
    analyzeExpr clause.Condition
