/// <reference types="react" />

declare module "*.svg" {
  import type { ComponentProps } from "react";
  import type Svg from "react-native-svg";

  export default function SvgComponent(
    props: ComponentProps<typeof Svg>,
  ): React.JSX.Element;
  export const Xml: (props: { xml: string }) => React.JSX.Element;
}
