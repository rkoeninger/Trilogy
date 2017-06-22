﻿module Trilogy.Parser

open System
open FParsec

let private pExpr, pExprRef = createParserForwardedToRef<SqlExpr, unit>()

let private pOperator =
    choice [
        stringReturn "="   Eq
        stringReturn ">"   Gt
        stringReturn "<"   Lt
        stringReturn "and" And
        stringReturn "or"  Or
    ]

let private pType =
    choice [
        stringReturn "bit"      Bit
        stringReturn "int"      Int
        stringReturn "varchar"  Varchar
        stringReturn "nvarchar" NVarchar
    ]

let rec private ident (x: string) =
    if x.StartsWith "@" then Param(x.Substring 1)
    elif x.Contains "." then
        let i = x.IndexOf "."
        Qualified(x.Substring(0, i), ident (x.Substring(i + 1, x.Length - i - 1)))
    else Named x

let private isIdentifierChar ch = Char.IsLetter ch || Char.IsDigit ch || ch = '_' || ch = '.' || ch = '@'

let private pIdentifier =
    choice [
        stringReturn "*" Star
        manySatisfy isIdentifierChar |>> ident
    ]

let private isShortIdentifierChar ch = Char.IsLetter ch || Char.IsDigit ch || ch = '_'

let private pShortIdentifier = manySatisfy isShortIdentifierChar

let private binary p0 pfill0 p1 f = tuple2 (p0 .>> pfill0) p1 |>> f

let private ternary p0 pfill0 p1 pfill1 p2 f = tuple3 (p0 .>> pfill0) (p1 .>> pfill1) p2 |>> f

let private pConst =
    choice [
        pint32 >>. preturn (ConstExpr Int)
        between (pchar '\'') (pchar '\'') (manySatisfy ((<>) '\'')) >>. preturn (ConstExpr Varchar)
    ]

let private pBinOp = // TODO: including in pExpr causes infinite loop?
    ternary
        pExpr
        spaces1
        pOperator
        spaces1
        pExpr
        BinaryExpr

let private pCast = // TODO: including in pExpr causes infinite loop?
    binary
        pExpr
        (spaces1 .>> pstring "as" .>> spaces1)
        pType
        CastExpr

let private pParens p = between (pchar '(' .>> spaces) (spaces >>. pchar ')') p

do pExprRef := choice [
    pParens pExpr
    //pBinOp
    //pCast
    pConst
    pIdentifier |>> IdExpr
]

let private pWhere =
    pstring "where" >>. spaces1 >>.
    pExpr

let private pJoin =
    pstring "join" >>. spaces1 >>.
    pIdentifier .>> spaces1 .>>
    pstring "on" .>> spaces1 .>>
    pExpr

let private pFrom =
    (pstring "from" >>. spaces1 >>. pIdentifier) .>>.
    many (attempt (spaces1 >>. pJoin))

let private pComma = spaces >>. pchar ',' >>. spaces

let private pSelect = pstring "select" >>. spaces1 >>. sepBy1 pExpr (attempt pComma)

let private pSelectStatement =
    choice [
        attempt
            (ternary
                pSelect
                spaces1
                pFrom
                spaces1
                pWhere
                (fun (exprs, ids, wh) ->
                  let (first, rest) = ids
                  SelectStatement {
                    Expressions = exprs;
                    Tables = List.map (fun x -> x.ToString()) (first :: rest);
                    Filter = wh
                  }))
        binary
            pSelect
            spaces1
            pFrom
            (fun (exprs, ids) ->
              let (first, rest) = ids
              SelectStatement {
                Expressions = exprs;
                Tables = List.map (fun x -> x.ToString()) (first :: rest);
                Filter = ConstExpr Int
              })
    ]

let private pInsertTable =
    pstring "insert" >>.
    spaces1 >>.
    pstring "into" >>.
    spaces1 >>.
    pShortIdentifier

let private pInsertColumns = pParens (sepBy1 pShortIdentifier (attempt pComma))

let private pInsertValues =
    pstring "values" >>.
    spaces >>.
    pParens (sepBy1 pExpr (attempt pComma))

let private pInsertStatement =
    ternary
        pInsertTable
        spaces
        pInsertColumns
        spaces
        pInsertValues
        (fun (tbl, cols, vals) ->
          InsertStatement {
            Table = tbl
            Columns = cols
            Values = vals
          })

let private pUpdateTable =
    pstring "update" >>.
    spaces1 >>.
    pShortIdentifier

let private pUpdateAssign =
    binary
        pShortIdentifier
        (attempt (spaces >>. pstring "=" >>. spaces))
        pExpr
        id

let private pSet =
    pstring "set" >>.
    spaces1 >>.
    sepBy pUpdateAssign (attempt pComma)

let private pUpdateStatement = 
    choice [
        attempt
            (ternary
                pUpdateTable
                spaces1
                pSet
                spaces1
                pWhere
                (fun (tbl, set, filter) ->
                  UpdateStatement {
                    Table = tbl
                    Assignments = set
                    Filter = filter
                  }))
        binary
            pUpdateTable
            spaces1
            pSet
            (fun (tbl, set) ->
              UpdateStatement {
                Table = tbl
                Assignments = set
                Filter = ConstExpr Int
              })
    ]

let private pDeleteTable =
    pstring "delete" >>.
    spaces1 >>.
    pstring "from" >>.
    spaces1 >>.
    pShortIdentifier

let private pDeleteStatement =
    binary
        pDeleteTable
        spaces1
        pWhere
        (fun (tbl, filter) ->
          DeleteStatement {
            Table = tbl
            Filter = filter
          })

let private pColumnDecl =
    binary
        pShortIdentifier
        spaces1
        pType
        id

let private pCreateStatement =
    pstring "create" >>.
    spaces1 >>.
    pstring "table" >>.
    spaces1 >>.
    (binary
        pShortIdentifier
        spaces
        (pParens (sepBy1 pColumnDecl (attempt pComma)))
        (fun (name, cols) ->
          CreateStatement {
            Name = name
            Columns = cols
          }))

let private pStatement =
    choice [
        pSelectStatement
        pInsertStatement
        pUpdateStatement
        pDeleteStatement
        pCreateStatement
    ]

let private pStatements = sepBy pStatement spaces1

let private runParser p s =
    match run p s with
    | Success(result, _, _) -> result
    | Failure(error, _, _) -> failwith error

let parse = runParser pStatement

let parseAll = runParser pStatements
