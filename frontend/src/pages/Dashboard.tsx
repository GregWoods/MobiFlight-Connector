import ProjectCard from "@/components/project/ProjectCard"
import {
  CardContent,
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card"
import { ProjectSummary } from "@/types/project"

const Dashboard = () => {
  const projectSummarys: ProjectSummary[] = [
    {
      Name: "Fenix A320",
      Sims: [{ Name: "MSFS2020", Available: true }, { Name: "FSUIPC", Available: false }],
      Controllers: [
        { Name: "Joystick", Type: "Thrustmaster T.16000M", Available: true },
        { Name: "Throttle", Type: "Thrustmaster TWCS", Available: false },
      ],
      Aircraft: [{ Name: "Fenix A320", Filter: "Airbus", Available: true }],
    },
    {
      Name: "Boeing 737",
      Sims: [{ Name: "MSFS2020", Available: true }],
      Controllers: [
        { Name: "Yoke", Type: "Logitech G Pro Flight", Available: true },
        { Name: "Throttle", Type: "Logitech G Pro Flight", Available: false },
      ],
      Aircraft: [{ Name: "Boeing 737", Filter: "Boeing", Available: false }],
    },
    {
      Name: "Airbus A380",
      Sims: [{ Name: "X-Plane", Available: false }],
      Controllers: [
        { Name: "Joystick", Type: "Thrustmaster T.16000M", Available: false },
        { Name: "Throttle", Type: "Thrustmaster TWCS", Available: false },
      ],
      Aircraft: [{ Name: "Airbus A380", Filter: "Airbus", Available: false }],
    },
  ]

  return (
    <div className="grid-flow grid grid-cols-2 grid-rows-3 gap-4">
      <Card className="border-shadow-none border-none shadow-none">
        <CardHeader>
          <CardTitle>Your Projects</CardTitle>
          <CardDescription>
            Quick access to your favorite projects
          </CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex flex-row gap-6">
            {projectSummarys.map((project) => (
              <ProjectCard
                key={project.Name}
                summary={project}
                className="w-72"
              />
            ))}
          </div>
        </CardContent>
      </Card>

      <Card className="border-shadow-none bg-muted row-span-3 rounded-none border-none shadow-none">
        <CardHeader>
          <CardTitle>MobiFlight Community</CardTitle>
          <CardDescription>
            Quick access to your favorite projects
          </CardDescription>
        </CardHeader>
        <CardContent>{/* Community content would go here */}</CardContent>
      </Card>

      <Card className="border-shadow-none border-none shadow-none">
        <CardHeader>
          <CardTitle>Your Controllers</CardTitle>
          <CardDescription>
            Quick access to your favorite projects
          </CardDescription>
        </CardHeader>
        <CardContent>{/* Controller list would go here */}</CardContent>
      </Card>
    </div>
  )
}

export default Dashboard
