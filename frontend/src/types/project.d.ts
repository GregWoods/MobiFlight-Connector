import { ConfigFile } from "./config"

export interface Project {
  Name: string
  FilePath: string
  ConfigFiles: ConfigFile[]
}

export interface ProjectSummary {
  Name: string
  Sims: {
    Name: string
    Available: boolean
  }[]
  Controllers: {
    Name: string
    Type: string
    Available: boolean
  }[]
  Aircraft: {
    Name: string
    Filter: string
    Available: boolean
  }[]
}
